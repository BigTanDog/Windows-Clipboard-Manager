using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClipboardManager.App.Imaging;
using ClipboardManager.App.Theme;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Core.AutoStart;
using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Hotkeys;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Retention;
using ClipboardManager.Core.Sensitive;
using ClipboardManager.Core.Settings;
using ClipboardManager.Core.Ui;
using ClipboardManager.Interop;
using ClipboardManager.Storage;

namespace ClipboardManager.App;

/// <summary>
/// 应用宿主：把消息窗口、剪贴板访问、存储与面板串起来。
/// <para>
/// 线程模型（AGENTS.md §3）：本类构造与 <see cref="Start"/> 运行在 WPF UI 线程（STA），
/// <b>所有剪贴板读写都发生在这条线程上</b>；解析、哈希、编码、写库交给后台任务，
/// 只传纯托管数据。
/// </para>
/// </summary>
internal sealed class AppHost : IDisposable
{
    /// <summary>面板最多展示的记录数。</summary>
    private const int MaxDisplayItems = 500;

    private readonly bool _diag;
    private readonly bool _interactive;
    private readonly int _durationSeconds;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly SelfWriteFilter _selfWrite = new();
    private readonly SemaphoreSlim _backgroundGate = new(1, 1);

    /// <summary>「当前剪贴板里到底是哪条记录」的记账本（面板徽标 + 删除即吊销的共同依据）。</summary>
    private readonly ClipboardPresenceTracker _presence = new();

    /// <summary>
    /// 敏感内容自动清空的计时器（附加项 B-09）：到点清空系统剪贴板。
    /// 只在 UI 线程上读写（剪贴板读写有 STA 亲和性），由 <see cref="ArmSensitiveClear"/> 设置间隔。
    /// </summary>
    private readonly DispatcherTimer _sensitiveClearTimer = new(DispatcherPriority.Background);

    /// <summary>待清空的剪贴板序列号（-1 表示没有排定）。到点按它复核，内容变了就放弃。</summary>
    private long _sensitiveClearSequence = -1;

    private AppLog _log = new(null);
    private SettingsStore? _settingsStore;
    private AppSettings _settings = AppSettings.Default;
    private SqliteHistoryRepository? _repository;
    private BlobStore? _blobs;
    private CaptureProcessor? _processor;
    private SensitiveMasker _masker = SensitiveMasker.Disabled;
    private MessageWindow? _messageWindow;
    private ClipboardAccess? _clipboard;
    private HotkeyManager? _hotkeys;
    private PanelWindow? _panel;
    private PasteService? _paste;
    private ClipboardRevoker? _revoker;
    private TrayIcon? _tray;
    private DispatcherTimer? _autoExitTimer;
    private DispatcherTimer? _panelWarmup;
    private IntPtr _previousForeground;
    private long _lastProcessedSequence = -1;
    private string _query = string.Empty;
    private string? _quotaWarning;

    /// <summary>设置窗口拖动亚克力滑杆时的临时预览值（未保存；null 表示用设置里的值）。</summary>
    private int? _acrylicPreview;
    private bool _disposed;

    /// <summary>创建宿主。</summary>
    /// <param name="diag">是否输出诊断日志（--diag）。</param>
    /// <param name="durationSeconds">自动退出秒数（0 表示不自动退出；用于指标测量）。</param>
    public AppHost(bool diag, int durationSeconds)
    {
        _diag = diag;
        _durationSeconds = durationSeconds;
        _interactive = !diag;

        _sensitiveClearTimer.Tick += (_, _) => OnSensitiveClearDue();
    }

    /// <summary>启动：数据目录自检 → 设置 → 数据库与本体目录 → 消息窗口与监听 → 热键 → READY → 预热。</summary>
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
            _tray?.Dispose();
            _hotkeys?.Dispose();
            _messageWindow?.StopClipboardListening();
            _messageWindow?.Dispose();
            _repository?.Dispose();
            _autoExitTimer?.Stop();
            _panelWarmup?.Stop();
            _sensitiveClearTimer.Stop();
            _backgroundGate.Dispose();
            _log.Info($"退出 | 运行 {(int)_uptime.Elapsed.TotalSeconds} 秒");
        }
        catch (Exception ex)
        {
            _log.Error("退出清理时出错", ex);
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
            + $"autoPaste={_settings.AutoPaste} maskSensitive={_settings.MaskSensitiveData} captureImages={_settings.CaptureImages} "
            + $"clearClipboardOnDelete={_settings.ClearClipboardOnDelete} hideOnClickOutside={_settings.HideOnClickOutside}");

        _repository = new SqliteHistoryRepository(AppPaths.DatabasePath);
        _repository.Initialize();
        _blobs = new BlobStore(AppPaths.DataDirectory, _log);
        _processor = new CaptureProcessor(_blobs, _log, _settings.CaptureImages);
        _log.Diag($"数据库就绪 | 现有 {_repository.CountAll()} 条");

        _masker = _settings.MaskSensitiveData ? new SensitiveMasker(true) : SensitiveMasker.Disabled;

        // 主题尽早应用：在任何窗口创建之前完成，避免出现"先亮后暗"的闪烁。
        var theme = ThemeManager.ApplyFromSetting(_settings.Theme);
        _log.Diag($"主题已应用：设置={_settings.Theme} 实际={theme}");

        // 启动时清理孤儿本体文件（此时没有在途写入，安全）。
        CleanOrphanBlobsAsync();

        _messageWindow = new MessageWindow();
        _messageWindow.Create();
        _messageWindow.ClipboardUpdated += OnClipboardUpdated;
        _messageWindow.HotkeyPressed += TogglePanel;
        _messageWindow.TrayMessage += OnTrayMessage;
        _messageWindow.TaskbarCreated += OnTaskbarCreated;
        _messageWindow.SystemSettingsChanged += OnSystemSettingsChanged;

        _clipboard = new ClipboardAccess(_messageWindow.Handle);
        _paste = new PasteService(_clipboard, _selfWrite, _blobs, _log);
        _revoker = new ClipboardRevoker(
            _clipboard,
            _presence,
            _log,
            hash => _repository.FindByHash(hash) is not null);

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

        _log.Info($"READY elapsedMs={_uptime.Elapsed.TotalMilliseconds:F0}");

        // 托盘图标：常驻通知区（需求 §3.5）。放在 READY 之后，避免拖慢冷启动指标。
        TryCreateTrayIcon();

        // 预热：READY 之后再创建面板窗口。
        // 实测依据：启动时创建窗口会让冷启动从 233ms 涨到 497ms，但不创建则首次弹出要多花 ~100ms。
        // 折中方案是在空闲时预热 —— 两个指标同时达标。
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

                // 借预热这个空闲点补一次剪贴板身份同步（重启后徽标与吊销判定才不会失准）。
                SyncClipboardPresence();
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

            // 同一次剪贴板变更系统会投递多条 WM_CLIPBOARDUPDATE（实测一次写入会来 3 条）。
            if (sequence == _lastProcessedSequence)
            {
                _log.Diag("剪贴板更新：序列号未变化，跳过重复通知");
                return;
            }

            // 自身写入（序列号判据）。
            if (_selfWrite.ShouldIgnore(sequence, null, DateTimeOffset.Now))
            {
                _log.Diag("剪贴板更新：自身写入（序列号命中），跳过");
                return;
            }

            if (!clipboard.TryReadPayload(out var payload, out var error))
            {
                if (error is not null)
                {
                    _log.Error("读取剪贴板失败：" + error);
                }
                else
                {
                    _log.Diag("剪贴板更新：没有受支持的格式，忽略");

                    // 剪贴板换成了我们不跟踪的内容：吊销判定靠实时序列号（天然安全），
                    // 但界面上那份列表还是旧的，面板正开着就顺手刷一下，
                    // 免得「剪贴板中」徽标停在已经失效的条目上。
                    if (_panel is { IsVisible: true })
                    {
                        RefreshListAsync();
                    }
                }

                return;
            }

            if (payload is null)
            {
                return;
            }

            // 已成功读取本条变更的内容，记录序列号：后续同一序列号的通知直接跳过。
            _lastProcessedSequence = sequence;

            var sourceApp = _settings.ExcludeByProcessName ? ClipboardAccess.TryGetSourceProcessName() : null;
            payload = payload with { SourceApp = sourceApp };

            _log.Diag($"捕获 {payload.Type} | 体积≈{payload.SizeBytes} 字节 | 来源={sourceApp ?? "未知"}");
            EnqueueCapture(payload, sequence);
        }
        catch (Exception ex)
        {
            _log.Error("处理剪贴板更新时出错", ex);
        }
    }

    /// <summary>后台串行处理：解析 → 哈希 → 编码 → 落本体 → 入库 → 淘汰 → 刷新列表。</summary>
    private void EnqueueCapture(ClipCandidate candidate, long sequence)
    {
        var maxItems = _settings.MaxItems;
        var sensitiveMinutes = _settings.ClearSensitiveAfterMinutes;

        _ = Task.Run(async () =>
        {
            await _backgroundGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var processed = _processor!.Process(candidate);
                if (processed is null)
                {
                    return;
                }

                // 哈希兜底判据（图片等类型的哈希要在解析后才算得出来）。
                if (_selfWrite.ShouldIgnore(sequence, processed.ContentHash, DateTimeOffset.Now))
                {
                    _log.Diag("剪贴板更新：自身写入（哈希命中），跳过存储");
                    return;
                }

                var outcome = _repository!.Upsert(
                    processed.Candidate,
                    processed.ContentHash,
                    processed.Preview,
                    processed.BlobPath,
                    processed.SizeBytes);

                // 登记「此刻剪贴板里装的就是这条记录」：面板徽标与删除时的吊销判定都靠它。
                _presence.NoteCaptured(sequence, outcome.Id);

                // B-09：含敏感信息的内容排定「到点自动清空剪贴板」。排定要动 DispatcherTimer，回 UI 线程做。
                var plan = SensitiveClearPlanner.Plan(
                    processed.Candidate,
                    sensitiveMinutes,
                    sequence,
                    DateTimeOffset.Now);
                if (plan is not null)
                {
                    _ = _dispatcher.BeginInvoke(() => ArmSensitiveClear(plan), DispatcherPriority.Background);
                }

                if (maxItems >= 0)
                {
                    var evicted = _repository.EnforceMaxItems(maxItems);
                    if (evicted.RemovedCount > 0)
                    {
                        _log.Diag($"条数上限淘汰 {evicted.RemovedCount} 条");
                        foreach (var blobPath in evicted.BlobPaths)
                        {
                            DeleteBlobFiles(blobPath);
                        }
                    }
                }

                _log.Diag(
                    $"入库 id={outcome.Id} 类型={processed.Candidate.Type} 新增={outcome.IsNew} 本体={processed.BlobPath ?? "无"}");

                var items = QueryItems();
                _ = _dispatcher.BeginInvoke(() => ApplyItems(items), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                _log.Error("处理剪贴板内容失败", ex);
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
                var items = QueryItems();
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

    private IReadOnlyList<ClipItem> QueryItems()
    {
        var limit = DisplayLimit();
        return string.IsNullOrEmpty(_query)
            ? _repository!.GetRecent(limit)
            : _repository!.Search(_query, limit);
    }

    private void ApplyItems(IReadOnlyList<ClipItem> items)
    {
        if (_panel is null)
        {
            // 面板尚未创建（懒创建）：跳过刷新，首次弹出时会重新读库。
            return;
        }

        var now = DateTimeOffset.Now;

        // 徽标判定要点：必须带上「实时序列号」—— 剪贴板一旦被任何程序改写，旧记账立刻失效，
        // 所以这里不能用缓存的序列号。
        var liveSequence = ClipboardAccess.GetSequenceNumber();
        _panel.SetItems(
        [
            .. items.Select(item => new ClipItemViewModel(
                item,
                _masker,
                now,
                _query,
                ThumbnailPath(item),
                _presence.IsCurrent(item.Id, liveSequence)))
        ]);
    }

    /// <summary>
    /// 缩略图绝对路径：<b>只有图片记录</b>才有。
    /// （HTML 记录也有本体文件，但它没有缩略图，若不加类型判断会让界面出现一个空的缩略图框。）
    /// </summary>
    private string? ThumbnailPath(ClipItem item) =>
        item.Type != ClipContentType.Image || string.IsNullOrEmpty(item.BlobPath)
            ? null
            : Path.Combine(AppPaths.DataDirectory, BlobStore.ThumbnailPathFor(item.BlobPath)!);

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
        panel.SearchRequested += OnSearchRequested;
        panel.PinToggleRequested += OnPinToggleRequested;
        panel.SettingsRequested += OnSettingsRequestedFromPanel;
        panel.SetQuotaHint(_quotaWarning);
        panel.Icon = Imaging.AppIcon.TryLoadImageSource();
        panel.HideOnClickOutside = _settings.HideOnClickOutside;
        panel.SingleClickPaste = _settings.SingleClickPaste;

        // 立即创建 HWND：让 WS_EX_TOOLWINDOW 在显示前生效，并让首次 Show() 更快。
        _ = new WindowInteropHelper(panel).EnsureHandle();

        _panel = panel;

        // 面板是无边框自绘窗口：圆角/标题栏配色向 DWM 申请，底色与亚克力走窗口合成。
        ApplyPanelAppearance();
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

        // 每次弹出都重置搜索（产品计划 Q-2 的既定行为），并展示完整列表。
        panel.ResetSearch();
        _query = string.Empty;
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

        _log.Info($"PANEL_SHOWN elapsedMs={watch.Elapsed.TotalMilliseconds:F1} dpi={dpi} firstShow={firstShow}");
    }

    private void HidePanel() => _panel?.Hide();

    /// <summary>面板底部齿轮：先收起面板再打开设置，避免设置窗口被置顶面板压住。</summary>
    private void OnSettingsRequestedFromPanel()
    {
        HidePanel();
        ShowSettingsDialog();
    }

    /// <summary>把窗口外观（深色标题栏 / 圆角）对齐当前生效主题。</summary>
    private void ApplyWindowAppearance(Window window)
    {
        var dark = string.Equals(ThemeManager.Current, Core.Theme.ThemeResolver.Dark, StringComparison.Ordinal);
        Interop.WindowAppearance.ApplyTheme(new WindowInteropHelper(window).Handle, dark);
    }

    /// <summary>
    /// 刷新面板外观：DWM 圆角与标题栏配色 + 底色与亚克力强度（B-04，含滑杆预览值）。
    /// DWM 模糊申请由面板自己负责（隐藏时只记账，显示渲染后再申请——见 PanelWindow.ApplyAcrylic）。
    /// </summary>
    private void ApplyPanelAppearance()
    {
        var panel = _panel;
        if (panel is null)
        {
            return;
        }

        ApplyWindowAppearance(panel);
        panel.ApplyAcrylic(
            _acrylicPreview ?? _settings.AcrylicStrength,
            ReadThemeColor("PanelBackgroundBrush"),
            ReadThemeColor("HeaderBackgroundBrush"));
    }

    /// <summary>设置窗口拖动强度滑杆时的即时预览（不落盘，关窗时由窗口回退）。</summary>
    private void PreviewAcrylic(int strength)
    {
        _acrylicPreview = AcrylicTint.Normalize(strength);
        ApplyPanelAppearance();
    }

    /// <summary>读取主题色（资源缺失时回退透明：亚克力自然失效，功能不受影响）。</summary>
    private static Color ReadThemeColor(string key) =>
        Application.Current?.TryFindResource(key) is SolidColorBrush brush ? brush.Color : Colors.Transparent;

    private void OnSearchRequested(string query)
    {
        _query = query;
        _log.Diag($"搜索：{(string.IsNullOrEmpty(query) ? "清空" : "执行")}");
        RefreshListAsync();
    }

    /// <summary>粘贴：按类型写回剪贴板 → 隐藏面板 → 恢复前台窗口 → 注入 Ctrl+V。</summary>
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

            if (!paste.TryCopyToClipboard(item, out var writeSequence, out var error))
            {
                _log.Error("粘贴失败：" + error);
                return;
            }

            // 剪贴板此刻装的就是这条记录 —— 必须同步身份：在面板里换一条，系统能粘贴的内容也跟着换，
            // 「剪贴板中」徽标与后续的吊销判定都要跟着走（用户 2026-09-19 特别指出的细节）。
            _presence.NoteCaptured(writeSequence, item.Id);
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

    /// <summary>
    /// 执行淘汰（需求 §3.3 + D-09）：先按条数上限、再按磁盘上限；两步都遵循
    /// 「非收藏优先淘汰、收藏永不自动删除」，只剩收藏仍超限时改为提示用户。
    /// </summary>
    private void ApplyRetention(int maxItems)
    {
        var repository = _repository!;

        var byCount = repository.EnforceMaxItems(maxItems);
        if (byCount.RemovedCount > 0)
        {
            _log.Diag($"条数上限淘汰 {byCount.RemovedCount} 条");
            foreach (var blobPath in byCount.BlobPaths)
            {
                DeleteBlobFiles(blobPath);
            }
        }

        var quotaMb = _settings.DiskQuotaMb;
        if (quotaMb < 0)
        {
            UpdateQuotaWarning(null);
            return;
        }

        var plan = RetentionPlanner.PlanByDiskQuota(
            repository.GetRetentionCandidates(),
            (long)quotaMb * 1024 * 1024);

        if (plan.EvictIds.Count > 0)
        {
            var blobPaths = repository.DeleteMany(plan.EvictIds);
            _log.Diag($"磁盘上限淘汰 {plan.EvictIds.Count} 条（合计 {FormatMegabytes(plan.TotalBytes)}）");
            foreach (var blobPath in blobPaths)
            {
                DeleteBlobFiles(blobPath);
            }
        }

        UpdateQuotaWarning(
            plan.OverQuotaAfterEviction
                ? $"收藏已占用 {FormatMegabytes(plan.PinnedBytes)}，超过磁盘上限 {quotaMb}MB。收藏不会被自动删除，请手动清理。"
                : null);
    }

    private void UpdateQuotaWarning(string? message)
    {
        if (string.Equals(_quotaWarning, message, StringComparison.Ordinal))
        {
            return;
        }

        _quotaWarning = message;
        var panel = _panel;
        if (panel is not null)
        {
            _ = _dispatcher.BeginInvoke(() => panel.SetQuotaHint(message), DispatcherPriority.Background);
        }
    }

    private static string FormatMegabytes(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? $"{bytes / (1024.0 * 1024 * 1024):0.#} GB"
            : $"{bytes / (1024.0 * 1024):0.#} MB";

    /// <summary>右键菜单切换收藏（需求 §3.3：收藏项永不参与自动淘汰）。</summary>
    private void OnPinToggleRequested(ClipItem item)
    {
        _ = Task.Run(async () =>
        {
            await _backgroundGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var pinned = !item.IsPinned;
                var changed = _repository!.SetPinned(item.Id, pinned);
                _log.Diag($"收藏切换 id={item.Id} → {pinned}（生效={changed}）");

                var items = QueryItems();
                _ = _dispatcher.BeginInvoke(() => ApplyItems(items), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                _log.Error("切换收藏状态失败", ex);
            }
            finally
            {
                _backgroundGate.Release();
            }
        });
    }

    private void OnDeleteRequested(ClipItem item)
    {
        _ = Task.Run(async () =>
        {
            await _backgroundGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // 先取记录（拿到本体路径），再删行，最后删文件 —— 顺序保证不会出现「文件没了但记录还在」。
                var record = _repository!.GetById(item.Id);
                var removed = _repository.Delete(item.Id);
                if (removed && record is not null)
                {
                    DeleteBlobFiles(record.BlobPath);
                }

                _log.Diag($"删除记录 id={item.Id} 结果={removed}");

                // 删除即吊销：真的删掉了才回 UI 线程处理剪贴板（剪贴板读写必须在 STA 线程）。
                if (removed && record is not null && _settings.ClearClipboardOnDelete)
                {
                    var deleted = record;
                    _ = _dispatcher.BeginInvoke(
                        () => RevokeClipboardAfterDelete(deleted),
                        DispatcherPriority.Background);
                }

                var items = QueryItems();
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

    // ────────────────────── 托盘（需求 §3.5） ──────────────────────

    private const int TrayCommandShowPanel = 1;
    private const int TrayCommandSettings = 2;
    private const int TrayCommandClearHistory = 3;
    private const int TrayCommandToggleAutoStart = 4;
    private const int TrayCommandExit = 5;

    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;

    /// <summary>创建托盘图标（READY 之后调用；资源管理器重启后也要重新创建）。</summary>
    private void TryCreateTrayIcon()
    {
        try
        {
            var tray = _tray ??= new TrayIcon(_messageWindow!.Handle, MessageWindow.TrayCallbackMessage);
            var tooltip = $"剪贴板管理器 · {_settings.Hotkey}";

            // 优先用应用图标（Assets/app.ico，与 exe/窗口图标同一份）；资源缺失时回退到运行时绘制的剪贴板图标。
            var embeddedIcon = Imaging.AppIcon.TryReadBytes();
            _log.Diag(embeddedIcon is null ? "托盘图标来源：运行时绘制（未找到嵌入资源）" : "托盘图标来源：嵌入资源 Assets/app.ico");
            var ico = embeddedIcon ?? TrayIconImage.BuildIco();

            if (!tray.TryAddFromIco(ico, tooltip, out var error))
            {
                _log.Error("创建托盘图标失败：" + error);
                return;
            }

            _log.Info("托盘图标已创建");
        }
        catch (Exception ex)
        {
            _log.Error("创建托盘图标异常", ex);
        }
    }

    /// <summary>资源管理器重启后托盘会被清空，必须重建（否则图标永久消失）。</summary>
    private void OnTaskbarCreated()
    {
        _log.Info("检测到资源管理器重启，重建托盘图标");

        // 旧句柄已随旧任务栏失效：释放后重新创建，避免 HICON 泄漏。
        _tray?.Dispose();
        _tray = null;
        TryCreateTrayIcon();
    }

    /// <summary>系统设置变化：跟随系统主题时立即重新解析（需求 §2 深浅色跟随）。</summary>
    private void OnSystemSettingsChanged()
    {
        if (!string.Equals(_settings.Theme, Core.Theme.ThemeResolver.System, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var theme = ThemeManager.ApplyFromSetting(_settings.Theme);
        ApplyPanelAppearance();

        _log.Diag($"系统主题变化，重新应用：{theme}");
    }

    private void OnTrayMessage(uint eventId, int x, int y)
    {
        _log.Diag($"托盘回调 | 事件=0x{eventId:X4} 坐标={x},{y}");

        switch (eventId)
        {
            // v4 语义下右键菜单事件是 WM_CONTEXTMENU，鼠标消息才是 WM_RBUTTONUP，两个都要接。
            case WmContextMenu:
            case WmRButtonUp:
                ShowTrayMenu(x, y);
                break;

            case WmLButtonUp:
            case WmLButtonDblClk:
                ShowPanel();
                break;

            default:
                break;
        }
    }

    private void ShowTrayMenu(int x, int y)
    {
        var tray = _tray;
        if (tray is null)
        {
            return;
        }

        var autoStartEnabled = AutoStartRegistry.IsEnabledWith(BuildAutoStartCommand(_settings));

        TrayMenuItem[] items =
        [
            new(TrayCommandShowPanel, "显示面板"),
            new(TrayCommandSettings, "设置…"),
            new(TrayCommandClearHistory, "清空历史…"),
            TrayMenuItem.Separator,
            new(TrayCommandToggleAutoStart, "开机自启", Checked: autoStartEnabled),
            TrayMenuItem.Separator,
            new(TrayCommandExit, "退出"),
        ];

        int command;
        try
        {
            _log.Diag($"弹出托盘菜单（{items.Length} 项）…");
            command = tray.ShowMenu(items, x, y);
            _log.Diag($"托盘菜单返回命令={command}");
        }
        catch (Exception ex)
        {
            _log.Error("显示托盘菜单失败", ex);
            return;
        }

        HandleTrayCommand(command);
    }

    private void HandleTrayCommand(int command)
    {
        switch (command)
        {
            case TrayCommandShowPanel:
                ShowPanel();
                break;

            case TrayCommandSettings:
                ShowSettingsDialog();
                break;

            case TrayCommandClearHistory:
                ClearHistoryWithConfirm();
                break;

            case TrayCommandToggleAutoStart:
                ApplySettings(_settings with { AutoStart = !AutoStartRegistry.IsEnabledWith(BuildAutoStartCommand(_settings)) });
                break;

            case TrayCommandExit:
                _log.Info("用户从托盘菜单退出");
                Application.Current.Shutdown();
                break;

            default:
                break;
        }
    }

    private static string BuildAutoStartCommand(AppSettings settings) =>
        AutoStartCommand.Build(AppPaths.ProcessPath ?? AppPaths.ProgramDirectory, settings.AutoStartDelaySeconds);

    /// <summary>一键清空（需求 §3.3：必须二次确认）。</summary>
    private void ClearHistoryWithConfirm()
    {
        var count = _repository?.CountAll() ?? 0;
        if (count == 0)
        {
            return;
        }

        // 删除前先判定：剪贴板里装的是不是我们历史里的某条记录 —— 记录一删就再也认不出身份了。
        var revokeClipboard = false;
        var clipboardSequence = 0L;
        if (_settings.ClearClipboardOnDelete && _revoker is not null)
        {
            try
            {
                revokeClipboard = _revoker.HoldsTrackedRecord(out clipboardSequence);
            }
            catch (Exception ex)
            {
                _log.Error("判定剪贴板是否属于历史失败", ex);
            }
        }

        // 破坏性操作先说清楚会波及什么：不能删完才让用户发现剪贴板被清了。
        var clipboardNotice = _settings.ClearClipboardOnDelete
            ? "\n若当前系统剪贴板里正是其中一条，剪贴板也会被一并清空。"
            : string.Empty;

        var answer = MessageBox.Show(
            $"确定要清空全部 {count} 条历史记录吗？\n收藏的记录也会被删除，且无法恢复。{clipboardNotice}",
            "剪贴板管理器",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await _backgroundGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var removed = _repository!.DeleteAll();

                // 记录已清空 → 所有本体文件都成了孤儿，直接在启动时的同一套 GC 逻辑里清掉。
                var orphans = _blobs!.CollectOrphans([]);
                _log.Info($"已清空历史：{removed} 条，清理本体文件 {orphans} 个");

                UpdateQuotaWarning(null);
                var items = QueryItems();
                _ = _dispatcher.BeginInvoke(() => ApplyItems(items), DispatcherPriority.Background);

                if (revokeClipboard)
                {
                    _ = _dispatcher.BeginInvoke(
                        () => RevokeClipboardAfterClearAll(clipboardSequence),
                        DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                _log.Error("清空历史失败", ex);
            }
            finally
            {
                _backgroundGate.Release();
            }
        });
    }

    /// <summary>打开设置窗口；保存后统一由 <see cref="ApplySettings"/> 生效并落盘。</summary>
    private void ShowSettingsDialog()
    {
        try
        {
            var window = new SettingsWindow(_settings, MeasureDiskUsage, Storage.AppPaths.BlobDirectory, PreviewAcrylic);

            // 录制快捷键时必须先摘掉全局热键：否则按下的组合会被系统直接吞掉（录不到），
            // 还会顺带把面板切出来。
            window.HotkeyRecordingChanged += OnHotkeyRecordingChanged;
            _ = window.ShowDialog();

            if (window.Result is { } updated)
            {
                ApplySettings(updated);
            }
        }
        catch (Exception ex)
        {
            _log.Error("打开设置窗口失败", ex);
        }
    }

    /// <summary>设置页进入/退出快捷键录制：暂停与恢复全局热键。</summary>
    private void OnHotkeyRecordingChanged(bool recording)
    {
        if (recording)
        {
            _hotkeys?.Unregister();
            _log.Diag("快捷键录制开始：暂停全局热键");
            return;
        }

        _log.Diag("快捷键录制结束：恢复全局热键");
        ReRegisterHotkey(_settings.Hotkey);
    }

    /// <summary>统计当前磁盘占用（设置窗口在后台线程调用；仓储自带锁，跨线程安全）。</summary>
    private DiskUsageInfo MeasureDiskUsage()
    {
        var count = 0;
        try
        {
            count = _repository?.CountAll() ?? 0;
        }
        catch (Exception ex)
        {
            _log.Error("统计记录条数失败", ex);
        }

        return DiskUsage.Measure(count);
    }

    /// <summary>删除记录后吊销剪贴板（UI 线程）；结果只在需要用户知情时给一行提示。</summary>
    private void RevokeClipboardAfterDelete(ClipItem deleted)
    {
        try
        {
            var result = _revoker?.RevokeIfMatches(deleted);
            if (result is null)
            {
                return;
            }

            switch (result.Status)
            {
                case RevokeStatus.Revoked:
                    _panel?.SetActionHint("已删除记录，并清空了系统剪贴板");
                    break;

                case RevokeStatus.Failed:
                    _panel?.SetActionHint("已删除记录；剪贴板被其它程序占用，未能一并清空");
                    break;

                default:
                    // 剪贴板里本来就是别的内容：只删记录，静默即可（这也是最常见的正常路径）。
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error("删除后吊销剪贴板失败", ex);
        }
    }

    /// <summary>「清空历史」完成后按条件清空剪贴板（UI 线程）。</summary>
    private void RevokeClipboardAfterClearAll(long sequence)
    {
        try
        {
            _ = _revoker?.ClearIfUnchanged(sequence, "清空历史");
        }
        catch (Exception ex)
        {
            _log.Error("清空历史后吊销剪贴板失败", ex);
        }
    }

    // ────────────────────── 敏感内容自动清空（附加项 B-09） ──────────────────────

    /// <summary>
    /// 排定（或按新内容重排）「敏感内容到点自动清空」。<b>只能在 UI 线程调用</b>（要动 DispatcherTimer）。
    /// <para>
    /// 只保留最近一次排定：又复制了一条敏感内容就按新时间重新计时。
    /// </para>
    /// </summary>
    /// <param name="plan">排定结果。</param>
    private void ArmSensitiveClear(SensitiveClearPlan plan)
    {
        var remaining = plan.Deadline - DateTimeOffset.Now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        _sensitiveClearSequence = plan.Sequence;
        _sensitiveClearTimer.Stop();
        _sensitiveClearTimer.Interval = remaining;
        _sensitiveClearTimer.Start();

        _log.Diag($"敏感内容计时：{plan.Minutes} 分钟后自动清空剪贴板（剩余 {remaining.TotalSeconds:F0}s）");
    }

    /// <summary>取消已排定的敏感内容清空（关闭功能、或当前剪贴板已不含敏感信息）。</summary>
    /// <param name="reason">取消原因（进日志）。</param>
    private void CancelSensitiveClear(string reason)
    {
        if (_sensitiveClearSequence < 0 && !_sensitiveClearTimer.IsEnabled)
        {
            return;
        }

        _sensitiveClearTimer.Stop();
        _sensitiveClearSequence = -1;
        _log.Diag("取消敏感内容自动清空：" + reason);
    }

    /// <summary>
    /// 到点执行：只有当剪贴板里仍是当初那条内容时才清空（序列号复核在剪贴板锁内完成，
    /// 所以"这段时间用户又复制了别的东西"时什么都不会发生）。
    /// </summary>
    private void OnSensitiveClearDue()
    {
        _sensitiveClearTimer.Stop();

        var sequence = _sensitiveClearSequence;
        _sensitiveClearSequence = -1;
        if (sequence < 0)
        {
            return;
        }

        try
        {
            var result = _revoker?.ClearIfUnchanged(sequence, "敏感内容定时清空");
            if (result is null)
            {
                return;
            }

            switch (result.Status)
            {
                case RevokeStatus.Revoked:
                    _panel?.SetActionHint("已自动清空含敏感信息的剪贴板");
                    break;

                case RevokeStatus.Failed:
                    _panel?.SetActionHint("剪贴板被其它程序占用，未能自动清空敏感内容");
                    break;

                default:
                    // 内容早被换掉了：本次作废，静默（用户已经复制了新东西，不该再打扰）。
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error("敏感内容自动清空失败", ex);
        }
    }

    /// <summary>
    /// 设置生效（B-09）：关闭则取消已排定；开启则按<b>当前剪贴板</b>重新排定一次 ——
    /// 这样刚打开开关时，手里正握着的那条敏感内容也会被清掉，而不是等下一次复制。
    /// </summary>
    private void ApplySensitiveClearSetting()
    {
        if (_settings.ClearSensitiveAfterMinutes <= 0)
        {
            CancelSensitiveClear("设置里已关闭");
            return;
        }

        var clipboard = _clipboard;
        if (clipboard is null)
        {
            return;
        }

        // 读剪贴板必须在 STA 线程 —— 本方法只从 UI 线程调用（ApplySettings 的运行路径）。
        if (!clipboard.TryReadPayloadWithSequence(out var payload, out var sequence, out var error) || payload is null)
        {
            if (!string.IsNullOrEmpty(error))
            {
                _log.Diag("敏感内容排定：读剪贴板失败（" + error + "）");
            }

            return;
        }

        var plan = SensitiveClearPlanner.Plan(payload, _settings.ClearSensitiveAfterMinutes, sequence, DateTimeOffset.Now);
        if (plan is null)
        {
            CancelSensitiveClear("当前剪贴板不含敏感信息");
            return;
        }

        ArmSensitiveClear(plan);
    }

    /// <summary>
    /// 启动后补一次「当前剪贴板到底是哪条记录」的身份同步。
    /// <para>
    /// 为什么需要：重启后剪贴板里通常还留着上一条内容，而启动不会产生 <c>WM_CLIPBOARDUPDATE</c>，
    /// 不补这一步的话「剪贴板中」徽标不会亮、删除时也走不到快速路径（虽有指纹兜底，但这里同步后
    /// 更准、更快）。读剪贴板在 UI 线程（STA 要求），指纹计算是纯 CPU，丢到后台避免卡界面。
    /// </para>
    /// </summary>
    private void SyncClipboardPresence()
    {
        try
        {
            var clipboard = _clipboard;
            if (clipboard is null)
            {
                return;
            }

            if (!clipboard.TryReadPayloadWithSequence(out var payload, out var sequence, out var error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    _log.Diag("启动同步剪贴板身份失败：" + error);
                }

                return;
            }

            if (payload is null)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    var fingerprint = ClipboardFingerprint.Of(payload);
                    if (fingerprint is null)
                    {
                        return;
                    }

                    var record = _repository?.FindByHash(fingerprint);
                    if (record is null)
                    {
                        _log.Diag("启动同步：当前剪贴板内容不在历史里");
                        return;
                    }

                    _presence.NoteCaptured(sequence, record.Id);
                    _log.Diag($"启动同步：当前剪贴板 = 记录 id={record.Id}");
                    _ = _dispatcher.BeginInvoke(RefreshListAsync, DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    _log.Error("启动同步剪贴板身份失败", ex);
                }
            });
        }
        catch (Exception ex)
        {
            _log.Error("启动同步剪贴板身份失败", ex);
        }
    }

    /// <summary>
    /// 应用新设置：落盘 + 逐项生效（热键重注册、脱敏开关、图片记录、主题、开机自启、淘汰上限）。
    /// </summary>
    /// <param name="updated">新设置。</param>
    private void ApplySettings(AppSettings updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        var previous = _settings;
        _settings = updated.Normalize();
        if (_settingsStore is not null && !_settingsStore.TrySave(_settings, out var saveError))
        {
            _log.Error("设置保存失败：" + saveError);
        }

        _log.Info(
            $"设置已更新 | maxItems={_settings.MaxItems} diskQuotaMb={_settings.DiskQuotaMb} hotkey={_settings.Hotkey} "
            + $"autoStart={_settings.AutoStart}({_settings.AutoStartDelaySeconds}s) captureImages={_settings.CaptureImages} "
            + $"maskSensitive={_settings.MaskSensitiveData} clearClipboardOnDelete={_settings.ClearClipboardOnDelete} "
            + $"sensitiveClearMinutes={_settings.ClearSensitiveAfterMinutes} theme={_settings.Theme}");

        // 1) 脱敏开关（D-13）
        _masker = _settings.MaskSensitiveData ? new SensitiveMasker(true) : SensitiveMasker.Disabled;

        // 2) 图片记录开关
        if (_processor is not null)
        {
            _processor.CaptureImages = _settings.CaptureImages;
        }

        // 3) 主题（面板标题栏/圆角/亚克力底色要跟着重新申请，DWM 属性不会随资源字典自动变）
        _ = ThemeManager.ApplyFromSetting(_settings.Theme);

        // 4) 面板外观与交互（B-01 自动隐藏、B-04 亚克力强度）：预览值让位给落盘值
        _acrylicPreview = null;
        if (_panel is not null)
        {
            _panel.HideOnClickOutside = _settings.HideOnClickOutside;
            _panel.SingleClickPaste = _settings.SingleClickPaste;
        }

        ApplyPanelAppearance();

        // 5) 开机自启
        ApplyAutoStart(_settings);

        // 6) 热键（仅在变化时重注册，避免无谓的中断）
        if (!string.Equals(previous.Hotkey, _settings.Hotkey, StringComparison.Ordinal))
        {
            ReRegisterHotkey(_settings.Hotkey);
        }

        // 7) 托盘提示里的热键文案同步
        _tray?.UpdateTooltip($"剪贴板管理器 · {_settings.Hotkey}");

        // 8) 上限变化立即生效（可能触发淘汰），并刷新列表让脱敏开关立刻可见
        _ = Task.Run(async () =>
        {
            await _backgroundGate.WaitAsync().ConfigureAwait(false);
            try
            {
                ApplyRetention(_settings.MaxItems);
                var items = QueryItems();
                _ = _dispatcher.BeginInvoke(() => ApplyItems(items), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                _log.Error("应用上限设置失败", ex);
            }
            finally
            {
                _backgroundGate.Release();
            }
        });

        // 9) 敏感内容自动清空（B-09）：关闭立即取消；开启则按「当前剪贴板」重新排定一次
        ApplySensitiveClearSetting();
    }

    private void ReRegisterHotkey(string hotkey)
    {
        if (_hotkeys is null)
        {
            return;
        }

        if (!HotkeySpec.TryParse(hotkey, out var spec, out var parseError) || spec is null)
        {
            _log.Error($"热键配置无效：{parseError}");
            return;
        }

        if (_hotkeys.TryRegister(spec, out var registerError))
        {
            _log.Info($"热键已更新：{spec}");
            return;
        }

        _log.Error(registerError ?? "注册新热键失败");
        if (_interactive)
        {
            MessageBox.Show(
                registerError ?? "注册热键失败",
                "剪贴板管理器",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>写入或删除 HKCU 自启项（需求 §3.5）。</summary>
    private void ApplyAutoStart(AppSettings settings)
    {
        if (settings.AutoStart)
        {
            var command = BuildAutoStartCommand(settings);
            if (!AutoStartRegistry.TrySet(command, out var error))
            {
                _log.Error("设置开机自启失败：" + error);
                if (_interactive)
                {
                    MessageBox.Show(
                        $"设置开机自启失败：{error}",
                        "剪贴板管理器",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            _log.Info($"已设置开机自启：{command}");
            return;
        }

        if (!AutoStartRegistry.TryRemove(out var removeError))
        {
            _log.Error("取消开机自启失败：" + removeError);
            return;
        }

        _log.Info("已取消开机自启");
    }

    /// <summary>删除本体文件与缩略图（幂等）。</summary>
    private void DeleteBlobFiles(string? blobPath)
    {
        _blobs?.TryDelete(blobPath);
        _blobs?.TryDelete(BlobStore.ThumbnailPathFor(blobPath));
    }

    /// <summary>启动时清理孤儿本体文件（不阻塞 READY）。</summary>
    private void CleanOrphanBlobsAsync()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var referenced = _repository!.GetReferencedBlobPaths();
                _blobs!.CollectOrphans(referenced);
            }
            catch (Exception ex)
            {
                _log.Error("清理孤儿本体文件失败", ex);
            }
        });
    }
}
