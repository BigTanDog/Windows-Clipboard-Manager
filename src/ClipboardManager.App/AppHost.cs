using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Hotkeys;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Sensitive;
using ClipboardManager.Core.Settings;
using ClipboardManager.Core.Text;
using ClipboardManager.Core.Ui;
using ClipboardManager.Interop;
using ClipboardManager.Storage;

namespace ClipboardManager.App;

/// <summary>
/// 应用宿主：把消息窗口、剪贴板访问、存储与面板串起来（阶段一最小闭环）。
/// <para>
/// 线程模型（AGENTS.md §3）：本类构造与 <see cref="Start"/> 运行在 WPF UI 线程（STA），
/// <b>所有剪贴板读写都发生在这条线程上</b>；哈希、写库、读库交给后台任务，只传纯托管数据。
/// </para>
/// </summary>
internal sealed class AppHost : IDisposable
{
    /// <summary>面板最多展示的记录数（即使设置为「不限制」，界面也只取这么多）。</summary>
    private const int MaxDisplayItems = 500;

    private readonly bool _diag;
    private readonly bool _interactive;
    private readonly int _durationSeconds;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly SelfWriteFilter _selfWrite = new();
    private readonly SemaphoreSlim _backgroundGate = new(1, 1);

    private AppLog _log = new(null);
    private SettingsStore? _settingsStore;
    private AppSettings _settings = AppSettings.Default;
    private SqliteHistoryRepository? _repository;
    private SensitiveMasker _masker = SensitiveMasker.Disabled;
    private MessageWindow? _messageWindow;
    private ClipboardAccess? _clipboard;
    private HotkeyManager? _hotkeys;
    private PanelWindow? _panel;
    private PasteService? _paste;
    private DispatcherTimer? _autoExitTimer;
    private DispatcherTimer? _panelWarmup;
    private IntPtr _previousForeground;
    private long _lastProcessedSequence = -1;
    private bool _disposed;

    /// <summary>创建宿主。</summary>
    /// <param name="diag">是否输出诊断日志（--diag）。</param>
    /// <param name="durationSeconds">自动退出秒数（0 表示不自动退出；用于指标测量）。</param>
    public AppHost(bool diag, int durationSeconds)
    {
        _diag = diag;
        _durationSeconds = durationSeconds;
        _interactive = !diag;
    }

    /// <summary>启动：数据目录自检 → 设置 → 数据库 → 消息窗口与监听 → 热键 → 面板。</summary>
    public void Start()
    {
        try
        {
            StartCore();
        }
        catch (Exception ex)
        {
            // 启动期故障必须留下完整证据（含堆栈）：发布版本没有行号，日志是唯一线索。
            _log.Error("启动失败", ex);
            throw;
        }
    }

    private void StartCore()
    {
        if (!AppPaths.TryEnsureWritable(out var pathError))
        {
            // 需求 §5.5：目录不可写时给出清晰提示，不静默降级到 %APPDATA%。
            throw new InvalidOperationException(pathError);
        }

        _log = new AppLog(AppPaths.LogDirectory, _diag);
        _log.Info($"启动 v{typeof(AppHost).Assembly.GetName().Version} | ProcessPath={AppPaths.ProcessPath}");
        _log.Diag($"目录对照 | ProgramDirectory={AppPaths.ProgramDirectory} | AppContext.BaseDirectory={AppPaths.BaseDirectory}");

        _settingsStore = new SettingsStore(AppPaths.SettingsPath, _log);
        _settings = _settingsStore.Load();
        _log.Diag(
            $"设置 | maxItems={_settings.MaxItems} diskQuotaMb={_settings.DiskQuotaMb} hotkey={_settings.Hotkey} "
            + $"autoPaste={_settings.AutoPaste} maskSensitive={_settings.MaskSensitiveData}");

        _repository = new SqliteHistoryRepository(AppPaths.DatabasePath);
        _repository.Initialize();
        _log.Diag($"数据库就绪 | 现有 {_repository.CountAll()} 条");

        _masker = _settings.MaskSensitiveData ? new SensitiveMasker(true) : SensitiveMasker.Disabled;

        _messageWindow = new MessageWindow();
        _messageWindow.Create();
        _messageWindow.ClipboardUpdated += OnClipboardUpdated;
        _messageWindow.HotkeyPressed += TogglePanel;

        _clipboard = new ClipboardAccess(_messageWindow.Handle);
        _paste = new PasteService(_clipboard, _selfWrite, _log);

        if (!_messageWindow.TryStartClipboardListening(out var listenError))
        {
            throw new InvalidOperationException(listenError);
        }

        _hotkeys = new HotkeyManager(_messageWindow.Handle);
        if (HotkeySpec.TryParse(_settings.Hotkey, out var spec, out var parseError) && spec is not null)
        {
            if (!_hotkeys.TryRegister(spec, out var registerError))
            {
                // 需求 §3.4：注册失败必须提示用户换键，不能静默失败。
                _log.Error(registerError ?? "注册热键失败");
                if (_interactive)
                {
                    // 自动化验证（--diag）下不弹窗：弹窗会阻塞且无法被脚本关闭。
                    MessageBox.Show(
                        registerError ?? "注册热键失败",
                        "剪贴板管理器",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            else
            {
                _log.Info($"热键已注册：{spec}");
            }
        }
        else
        {
            _log.Error($"热键配置无效：{parseError}");
        }

        // 面板窗口<b>不在启动时创建</b>：实测常驻内存与冷启动都被 WPF 窗口/渲染栈拖高
        // （见阶段一实测数据），改为首次弹出时懒创建；首次弹出仍有充足余量（实测 ~77ms < 100ms）。
        // 列表在面板首次弹出时再读取。

        _log.Info($"READY elapsedMs={_uptime.Elapsed.TotalMilliseconds:F0}");

        // 预热：READY 之后再创建面板窗口。
        // 实测依据：启动时创建窗口会让冷启动从 233ms 涨到 497ms，但不创建则首次弹出要多花 ~100ms
        // （含窗口与渲染栈初始化）。折中方案是在空闲时预热 —— 两个指标同时达标。
        _panelWarmup = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(1.5),
        };
        _panelWarmup.Tick += (_, _) =>
        {
            _panelWarmup?.Stop();
            try
            {
                EnsurePanel();
                _log.Diag("面板窗口预热完成");
            }
            catch (Exception ex)
            {
                _log.Error("面板窗口预热失败", ex);
            }
        };
        _panelWarmup.Start();

        if (_durationSeconds > 0)
        {
            _autoExitTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(_durationSeconds),
            };
            _autoExitTimer.Tick += (_, _) =>
            {
                _autoExitTimer?.Stop();
                _log.Info("到达 --duration 指定时长，自动退出");
                Application.Current.Shutdown();
            };
            _autoExitTimer.Start();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 退出顺序（技术设计 §2.2）：注销热键 → 注销监听 → 销毁消息窗口 → 释放数据库。
        try
        {
            _hotkeys?.Dispose();
            _messageWindow?.StopClipboardListening();
            _messageWindow?.Dispose();
            _repository?.Dispose();
            _autoExitTimer?.Stop();
            _panelWarmup?.Stop();
            _backgroundGate.Dispose();
            _log.Info($"退出 | 运行 {(int)_uptime.Elapsed.TotalSeconds} 秒");
        }
        catch (Exception ex)
        {
            _log.Error("退出清理时出错", ex);
        }
    }

    /// <summary>WM_CLIPBOARDUPDATE 处理（STA 线程）。</summary>
    private void OnClipboardUpdated()
    {
        try
        {
            var clipboard = _clipboard;
            if (clipboard is null)
            {
                return;
            }

            var sequence = ClipboardAccess.GetSequenceNumber();

            // 同一次剪贴板变更系统会投递多条 WM_CLIPBOARDUPDATE（实测一次 SetClipboard 会来 3 条），
            // 序列号相同即同一次变更，处理过一次就跳过，避免重复读取与重复写库。
            if (sequence == _lastProcessedSequence)
            {
                _log.Diag("剪贴板更新：序列号未变化，跳过重复通知");
                return;
            }

            if (!clipboard.TryReadText(out var text, out var error))
            {
                if (error is not null)
                {
                    _log.Error("读取剪贴板失败：" + error);
                }
                else
                {
                    _log.Diag("剪贴板更新：无文本格式，忽略（阶段一只处理文本）");
                }

                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                _log.Diag("剪贴板更新：内容为空，忽略");
                return;
            }

            // 已成功读取本条变更的内容，记录序列号：后续同一序列号的通知直接跳过。
            _lastProcessedSequence = sequence;

            var hash = ContentHasher.ForText(text);
            if (_selfWrite.ShouldIgnore(sequence, hash, DateTimeOffset.Now))
            {
                _log.Diag("剪贴板更新：判定为自身写入，跳过（自循环防护）");
                return;
            }

            var sourceApp = _settings.ExcludeByProcessName ? ClipboardAccess.TryGetSourceProcessName() : null;
            var candidate = new ClipCandidate
            {
                Type = ClipContentType.Text,
                Text = text,
                SourceApp = sourceApp,
                CapturedAt = DateTimeOffset.Now,
            };

            _log.Diag($"捕获文本 | {text.Length} 字符 | 来源={sourceApp ?? "未知"}");
            EnqueueStore(candidate, hash);
        }
        catch (Exception ex)
        {
            _log.Error("处理剪贴板更新时出错", ex);
        }
    }

    /// <summary>入库 + 淘汰 + 刷新列表（全部在后台串行执行）。</summary>
    private void EnqueueStore(ClipCandidate candidate, string contentHash)
    {
        var preview = PreviewBuilder.ForText(candidate.Text);
        var maxItems = _settings.MaxItems;

        _ = Task.Run(async () =>
        {
            await _backgroundGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var outcome = _repository!.Upsert(candidate, contentHash, preview);
                if (maxItems >= 0)
                {
                    var removed = _repository.EnforceMaxItems(maxItems);
                    if (removed > 0)
                    {
                        _log.Diag($"条数上限淘汰 {removed} 条");
                    }
                }

                _log.Diag($"入库 id={outcome.Id} 新增={outcome.IsNew}");
                var items = _repository.GetRecent(DisplayLimit());
                _ = _dispatcher.BeginInvoke(() => ApplyItems(items), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                _log.Error("写入历史失败", ex);
            }
            finally
            {
                _backgroundGate.Release();
            }
        });
    }

    /// <summary>后台读取列表并刷新 UI（UI 线程只做视图模型构造）。</summary>
    private void RefreshListAsync()
    {
        _ = Task.Run(async () =>
        {
            await _backgroundGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var items = _repository!.GetRecent(DisplayLimit());
                _ = _dispatcher.BeginInvoke(() => ApplyItems(items), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                _log.Error("读取历史失败", ex);
            }
            finally
            {
                _backgroundGate.Release();
            }
        });
    }

    private void ApplyItems(IReadOnlyList<ClipItem> items)
    {
        if (_panel is null)
        {
            // 面板尚未创建（懒创建）：跳过刷新，首次弹出时会重新读库。
            return;
        }

        var now = DateTimeOffset.Now;
        _panel.SetItems([.. items.Select(item => new ClipItemViewModel(item, _masker, now))]);
    }

    private int DisplayLimit() => _settings.MaxItems < 0
        ? MaxDisplayItems
        : Math.Min(_settings.MaxItems, MaxDisplayItems);

    private void TogglePanel()
    {
        if (_panel is { IsVisible: true })
        {
            HidePanel();
        }
        else
        {
            ShowPanel();
        }
    }

    /// <summary>懒创建面板窗口（首次弹出时）。</summary>
    private PanelWindow EnsurePanel()
    {
        if (_panel is not null)
        {
            return _panel;
        }

        var panel = new PanelWindow();
        panel.PasteRequested += OnPasteRequested;
        panel.DeleteRequested += OnDeleteRequested;
        panel.CloseRequested += HidePanel;

        // 立即创建 HWND：让 WS_EX_TOOLWINDOW 在显示前生效，并让首次 Show() 更快。
        _ = new WindowInteropHelper(panel).EnsureHandle();
        _panel = panel;
        return panel;
    }

    /// <summary>显示面板：记录前台窗口 → 定位右下角 → 显示并取焦点 → 记录弹出耗时。</summary>
    private void ShowPanel()
    {
        var firstShow = _panel is null;
        var panel = EnsurePanel();
        if (panel.IsVisible)
        {
            return;
        }

        RefreshListAsync();

        var watch = Stopwatch.StartNew();

        // 必须在面板激活之前记录，否则拿到的就是面板自己（D-03）。
        _previousForeground = ForegroundWindowService.Capture();

        var handle = new WindowInteropHelper(panel).Handle;
        var dpi = ScreenLocator.GetDpiForWindow(handle);
        if (ScreenLocator.TryGetCursorWorkArea(out var workArea))
        {
            var (left, top) = PanelPositioning.ComputeBottomRight(
                workArea.Right,
                workArea.Bottom,
                panel.Width,
                panel.Height,
                dpi);
            panel.Left = left;
            panel.Top = top;
        }

        panel.Show();
        panel.Activate();
        panel.FocusList();

        // 等待渲染优先级队列排空：此时首帧已提交，耗时才有意义。
        panel.Dispatcher.Invoke(static () => { }, DispatcherPriority.Render);
        watch.Stop();

        _log.Info($"PANEL_SHOWN elapsedMs={watch.Elapsed.TotalMilliseconds:F1} dpi={dpi}");
    }

    private void HidePanel() => _panel?.Hide();

    /// <summary>粘贴：写回剪贴板 → 隐藏面板 → 恢复前台窗口 → 注入 Ctrl+V。</summary>
    private async void OnPasteRequested(ClipItem item)
    {
        try
        {
            var paste = _paste;
            if (paste is null)
            {
                return;
            }

            _log.Diag($"粘贴请求 id={item.Id} 类型={item.Type}");

            if (!paste.TryCopyToClipboard(item, out var error))
            {
                _log.Error("粘贴失败：" + error);
                return;
            }

            _log.Diag("已写回剪贴板并登记自身写入序列号");
            HidePanel();

            var target = _previousForeground;
            if (!_settings.AutoPaste)
            {
                _log.Diag("自动粘贴已关闭，内容已复制到剪贴板");
                return;
            }

            _log.Diag(
                $"自动粘贴：目标={WindowDiagnostics.Describe(target)} 当前前台={WindowDiagnostics.Describe(ForegroundWindowService.Current)}");

            if (!ForegroundWindowService.TryRestore(target))
            {
                // 兜底：托盘气泡属阶段三，这里先记录（内容已在剪贴板，用户可手动粘贴）。
                _log.Error("无法恢复先前窗口，内容已复制到剪贴板，请手动 Ctrl+V");
                return;
            }

            // 关键：必须确认前台窗口确实切到了目标窗口，否则注入的 Ctrl+V 会落到别的程序里。
            // 实测出现过「SetForegroundWindow 返回成功但前台未切换」的竞态，所以这里做轮询确认。
            for (var attempt = 0; attempt < 4 && !ForegroundWindowService.IsForeground(target); attempt++)
            {
                await Task.Delay(40).ConfigureAwait(true);
            }

            if (!ForegroundWindowService.IsForeground(target))
            {
                _log.Error(
                    "前台窗口未切换到目标窗口（目标=" + WindowDiagnostics.Describe(target)
                    + "，当前=" + WindowDiagnostics.Describe(ForegroundWindowService.Current) + "），已取消自动粘贴，内容已复制到剪贴板");
                return;
            }

            var injected = InputInjector.SendCtrlV();
            _log.Diag($"自动粘贴注入完成：成功={injected} 注入时前台={WindowDiagnostics.Describe(ForegroundWindowService.Current)}");

            if (!injected)
            {
                _log.Error("自动粘贴被系统阻止（目标程序权限更高），内容已复制到剪贴板，请手动 Ctrl+V");
            }
        }
        catch (Exception ex)
        {
            _log.Error("粘贴流程异常", ex);
        }
    }

    private void OnDeleteRequested(ClipItem item)
    {
        _ = Task.Run(async () =>
        {
            await _backgroundGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var removed = _repository!.Delete(item.Id);
                _log.Diag($"删除记录 id={item.Id} 结果={removed}");
                var items = _repository.GetRecent(DisplayLimit());
                _ = _dispatcher.BeginInvoke(() => ApplyItems(items), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                _log.Error("删除记录失败", ex);
            }
            finally
            {
                _backgroundGate.Release();
            }
        });
    }
}

      