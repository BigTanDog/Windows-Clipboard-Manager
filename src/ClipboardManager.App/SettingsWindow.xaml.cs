using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClipboardManager.App.Theme;
using ClipboardManager.Core.Hotkeys;
using ClipboardManager.Core.Settings;
using ClipboardManager.Core.Theme;

namespace ClipboardManager.App;

/// <summary>
/// 设置窗口（需求 §3.5：条数上限、快捷键、开机自启、排除应用、是否记录图片；外加 D-09 磁盘上限与 D-13 脱敏开关）。
/// <para>
/// 只负责收集与校验用户输入，<b>不做任何持久化与生效动作</b> —— 由 <see cref="AppHost"/> 统一应用，
/// 避免"设置窗口"和"运行态"两处各改一半。
/// </para>
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>条数档位的显示文案（与 <see cref="AppSettings.MaxItemsOptions"/> 一一对应）。</summary>
    private static readonly string[] MaxItemsLabels = ["50 条", "100 条", "200 条", "500 条", "不限制"];

    /// <summary>磁盘档位的显示文案（与 <see cref="AppSettings.DiskQuotaMbOptions"/> 一一对应）。</summary>
    private static readonly string[] DiskQuotaLabels = ["200 MB", "500 MB", "1 GB", "2 GB", "不限制"];

    /// <summary>主题档位文案（与 <see cref="ThemeResolver"/> 取值一一对应）。</summary>
    private static readonly string[] ThemeLabels = ["跟随系统", "浅色", "深色"];

    private static readonly string[] ThemeValues = [ThemeResolver.System, ThemeResolver.Light, ThemeResolver.Dark];

    private readonly ObservableCollection<string> _excludedApps = [];
    private readonly Func<DiskUsageInfo> _measureUsage;
    private readonly string _cacheDirectory;
    private readonly Action<int> _previewAcrylic;
    private readonly string _originalTheme;
    private readonly int _originalAcrylic;
    private readonly DispatcherTimer _usageTimer;
    private bool _usageBusy;
    private bool _themeReverted;
    private bool _recordingHotkey;
    private string _hotkeyBeforeRecording = string.Empty;

    /// <summary>创建设置窗口。</summary>
    /// <param name="current">当前设置（用于回显）。</param>
    /// <param name="measureUsage">统计磁盘占用的回调（在后台线程调用，避免大目录卡住界面）。</param>
    /// <param name="cacheDirectory">「打开缓存文件夹」的目标目录。</param>
    /// <param name="previewAcrylic">亚克力强度即时预览回调（不落盘；关窗取消时由本窗口回退）。</param>
    public SettingsWindow(AppSettings current, Func<DiskUsageInfo> measureUsage, string cacheDirectory, Action<int> previewAcrylic)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(measureUsage);
        ArgumentNullException.ThrowIfNull(cacheDirectory);
        ArgumentNullException.ThrowIfNull(previewAcrylic);

        _measureUsage = measureUsage;
        _cacheDirectory = cacheDirectory;
        _previewAcrylic = previewAcrylic;
        _originalTheme = current.Theme;
        _originalAcrylic = current.AcrylicStrength;

        InitializeComponent();

        // 底部版本号：用户要求设置窗口最下方能看到当前文件版本（版本号只来自 Directory.Build.props）。
        VersionText.Text = "版本 " + AppVersion.Display;

        MaxItemsBox.ItemsSource = MaxItemsLabels;
        DiskQuotaBox.ItemsSource = DiskQuotaLabels;
        ThemeBox.ItemsSource = ThemeLabels;
        ExcludedListBox.ItemsSource = _excludedApps;

        MaxItemsBox.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.MaxItemsOptions, current.MaxItems));
        DiskQuotaBox.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.DiskQuotaMbOptions, current.DiskQuotaMb));
        ThemeBox.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeValues, current.Theme));

        CaptureImagesBox.IsChecked = current.CaptureImages;
        AutoPasteBox.IsChecked = current.AutoPaste;
        MaskBox.IsChecked = current.MaskSensitiveData;
        ClearClipboardOnDeleteBox.IsChecked = current.ClearClipboardOnDelete;
        ExcludeByProcessBox.IsChecked = current.ExcludeByProcessName;
        HideOnClickOutsideBox.IsChecked = current.HideOnClickOutside;
        SingleClickPasteBox.IsChecked = current.SingleClickPaste;
        HotkeyBox.Text = current.Hotkey;
        AcrylicSlider.Value = current.AcrylicStrength;

        // 开机自启回显以「注册表里的真实状态」为准，避免设置文件与实际不一致时误导用户。
        AutoStartBox.IsChecked = Interop.AutoStartRegistry.IsEnabledWith(
            Core.AutoStart.AutoStartCommand.Build(Storage.AppPaths.ProcessPath ?? string.Empty, current.AutoStartDelaySeconds));
        AutoStartDelayBox.Text = current.AutoStartDelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        foreach (var app in current.ExcludedApps)
        {
            _excludedApps.Add(app);
        }

        // 主题：改档位即时预览（取消或直接关窗会回退），标题栏深色跟随生效主题
        ThemeBox.SelectionChanged += OnThemeSelectionChanged;
        SourceInitialized += OnSourceInitialized;

        // 占用统计：定期刷新，边用边看到底占了多少存储
        _usageTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
        _usageTimer.Tick += (_, _) => RefreshUsage();
        _usageTimer.Start();
        RefreshUsage();
    }

    /// <summary>窗口首次显示：套用应用图标 + 与主题匹配的标题栏外观。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Icon = Imaging.AppIcon.TryLoadImageSource();
        ApplyWindowAppearance();
    }

    /// <summary>把窗口外观（深色标题栏 / 圆角）对齐当前生效主题。</summary>
    private void ApplyWindowAppearance()
    {
        var dark = string.Equals(ThemeManager.Current, ThemeResolver.Dark, StringComparison.Ordinal);
        Interop.WindowAppearance.ApplyTheme(new WindowInteropHelper(this).Handle, dark);
    }

    /// <summary>主题下拉改动：立即预览（只改主题，不落盘）。</summary>
    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedIndex < 0)
        {
            return;
        }

        _ = ThemeManager.ApplyFromSetting(ThemeValues[ThemeBox.SelectedIndex]);
        ApplyWindowAppearance();
    }

    /// <summary>刷新磁盘占用：统计放到后台线程，完成后回 UI 线程更新文案。</summary>
    private void RefreshUsage()
    {
        if (_usageBusy)
        {
            return;
        }

        _usageBusy = true;
        var measure = _measureUsage;

        _ = Task.Run(measure).ContinueWith(
            task =>
            {
                _usageBusy = false;
                if (task.Status != TaskStatus.RanToCompletion)
                {
                    return;
                }

                var usage = task.Result;
                if (Dispatcher.CheckAccess())
                {
                    ShowUsage(usage);
                }
                else
                {
                    Dispatcher.Invoke(() => ShowUsage(usage));
                }
            },
            TaskScheduler.Default);
    }

    private void ShowUsage(DiskUsageInfo usage)
    {
        var index = DiskQuotaBox.SelectedIndex;
        var limit = index >= 0 && index < AppSettings.DiskQuotaMbOptions.Length
            ? AppSettings.DiskQuotaMbOptions[index]
            : AppSettings.DiskQuotaMbOptions[0];

        UsageText.Text = "当前占用：" + usage.Format(limit);
        UsageDetailText.Text = usage.Describe();
    }

    /// <summary>亚克力强度滑杆：更新数值文案并即时预览（不落盘）。</summary>
    private void OnAcrylicStrengthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AcrylicValueText is null)
        {
            // 初始化阶段控件还没建好（Slider 在 XAML 里先于 TextBlock 赋值）
            return;
        }

        var strength = (int)Math.Round(e.NewValue);
        AcrylicValueText.Text = strength <= 0 ? "0（不透明）" : strength.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _previewAcrylic(strength);
    }

    // ─────────────── 全局快捷键：按键录制 ───────────────

    /// <summary>
    /// 快捷键进入 / 退出录制状态。
    /// 宿主收到 <c>true</c> 必须**先注销全局热键**，否则录制时按下的组合会被系统直接吞掉、
    /// 根本到不了这里（也会顺带把面板切出来）。
    /// </summary>
    public event Action<bool>? HotkeyRecordingChanged;

    /// <summary>录制中的提示文案（也是"当前没有有效组合"的判据）。</summary>
    private const string RecordingPlaceholder = "请按下组合键…（Esc 取消）";

    /// <summary>快捷键框获得焦点：进入录制状态。</summary>
    private void OnHotkeyBoxGotFocus(object sender, RoutedEventArgs e) => StartHotkeyRecording();

    /// <summary>
    /// 鼠标按下也进入录制：录制成功后会停止录制，此时框仍然有焦点，
    /// 再点一次不会触发 GotFocus —— 不补这个入口用户就会觉得"第二次点没反应"。
    /// 注意不要设置 <c>e.Handled</c>，否则点击无法把焦点交给输入框。
    /// </summary>
    private void OnHotkeyBoxMouseDown(object sender, MouseButtonEventArgs e) => StartHotkeyRecording();

    private void StartHotkeyRecording()
    {
        if (_recordingHotkey)
        {
            return;
        }

        _recordingHotkey = true;
        _hotkeyBeforeRecording = HotkeyBox.Text;
        HotkeyBox.Text = RecordingPlaceholder;
        HotkeyBox.BorderBrush = (Brush)FindResource("AccentBrush");
        HotkeyRecordingChanged?.Invoke(true);
    }

    /// <summary>失去焦点：结束录制（没录到有效组合就还原原值）。</summary>
    private void OnHotkeyBoxLostFocus(object sender, RoutedEventArgs e) => StopHotkeyRecording();

    /// <summary>录制中：把按下的键组合翻译成热键文本。</summary>
    private void OnHotkeyBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recordingHotkey)
        {
            return;
        }

        e.Handled = true;

        // Alt 组合下主键会走 SystemKey（Key.System），否则拿到的是修饰键本身
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            CancelHotkeyRecording();
            return;
        }

        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            // 只按了修饰键：继续等主键
            HotkeyBox.Text = "请再按一个键…（Esc 取消）";
            return;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (HotkeySpec.TryCreate(virtualKey, CurrentModifiers(), out var spec, out var error) && spec is not null)
        {
            HotkeyBox.Text = spec.ToString();
            _hotkeyBeforeRecording = HotkeyBox.Text;
            StatusText.Text = "快捷键已更新为 " + HotkeyBox.Text + "（保存后生效）";
        }
        else
        {
            HotkeyBox.Text = error ?? "组合键无效";
        }

        StopHotkeyRecording();
    }

    /// <summary>当前按下的修饰键。</summary>
    private static HotkeyModifiers CurrentModifiers()
    {
        var modifiers = Keyboard.Modifiers;
        var result = HotkeyModifiers.None;

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= HotkeyModifiers.Control;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= HotkeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= HotkeyModifiers.Win;
        }

        return result;
    }

    private void CancelHotkeyRecording()
    {
        HotkeyBox.Text = _hotkeyBeforeRecording;
        StopHotkeyRecording();
    }

    private void StopHotkeyRecording()
    {
        if (!_recordingHotkey)
        {
            return;
        }

        _recordingHotkey = false;

        // 还停在提示文案上说明没录到东西：还原原值，避免把提示当热键存下去
        if (HotkeyBox.Text is RecordingPlaceholder or "请再按一个键…（Esc 取消）")
        {
            HotkeyBox.Text = _hotkeyBeforeRecording;
        }

        HotkeyBox.ClearValue(BorderBrushProperty);
        HotkeyRecordingChanged?.Invoke(false);
    }

    /// <summary>打开缓存目录（图片 / 富文本本体所在位置）。</summary>
    private void OnOpenCacheFolderClick(object sender, RoutedEventArgs e)
    {
        StatusText.Text = DiskUsage.OpenFolder(_cacheDirectory)
            ? "已打开缓存文件夹"
            : "打开失败，请手动前往：" + _cacheDirectory;
    }

    /// <summary>关窗收尾：停掉刷新计时器；未保存的主题预览要回退，避免"看着改了其实没保存"。</summary>
    protected override void OnClosed(EventArgs e)
    {
        _usageTimer.Stop();

        if (Result is null && !_themeReverted)
        {
            _themeReverted = true;
            _ = ThemeManager.ApplyFromSetting(_originalTheme);
            _previewAcrylic(_originalAcrylic);
        }

        base.OnClosed(e);
    }

    /// <summary>用户点击保存后的结果；取消时为 null。</summary>
    public AppSettings? Result { get; private set; }

    private void OnAddExcludedClick(object sender, RoutedEventArgs e)
    {
        var value = ExcludedInput.Text.Trim();
        if (value.Length == 0)
        {
            return;
        }

        // 进程名只取文件名部分，避免用户粘贴整条路径
        value = System.IO.Path.GetFileName(value);

        if (value.Length > 0
            && !_excludedApps.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
        {
            _excludedApps.Add(value);
        }

        ExcludedInput.Text = string.Empty;
    }

    private void OnRemoveExcludedClick(object sender, RoutedEventArgs e)
    {
        if (ExcludedListBox.SelectedItem is string selected)
        {
            _excludedApps.Remove(selected);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryBuild(out var settings, out var error) || settings is null)
        {
            StatusText.Text = error ?? "设置有误";
            return;
        }

        Result = settings;
        Close();
    }

    /// <summary>收集并校验界面输入；校验失败时给出可读原因（不静默纠正）。</summary>
    private bool TryBuild(out AppSettings? settings, out string? error)
    {
        settings = null;
        error = null;

        if (MaxItemsBox.SelectedIndex < 0 || DiskQuotaBox.SelectedIndex < 0 || ThemeBox.SelectedIndex < 0)
        {
            error = "请选择所有档位";
            return false;
        }

        var hotkeyText = HotkeyBox.Text.Trim();
        if (!HotkeySpec.TryParse(hotkeyText, out var spec, out var hotkeyError) || spec is null)
        {
            error = $"快捷键无效：{hotkeyError}";
            return false;
        }

        var delayText = AutoStartDelayBox.Text.Trim();
        if (!int.TryParse(delayText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var delay)
            || delay < 0
            || delay > Core.AutoStart.AutoStartCommand.MaxDelaySeconds)
        {
            error = $"延迟必须是 0–{Core.AutoStart.AutoStartCommand.MaxDelaySeconds} 的整数";
            return false;
        }

        var theme = ThemeValues[ThemeBox.SelectedIndex];

        settings = new AppSettings
        {
            MaxItems = AppSettings.MaxItemsOptions[MaxItemsBox.SelectedIndex],
            DiskQuotaMb = AppSettings.DiskQuotaMbOptions[DiskQuotaBox.SelectedIndex],
            Hotkey = spec.ToString(),
            AutoPaste = AutoPasteBox.IsChecked == true,
            AutoStart = AutoStartBox.IsChecked == true,
            AutoStartDelaySeconds = delay,
            ExcludedApps = [.. _excludedApps],
            ExcludeByProcessName = ExcludeByProcessBox.IsChecked == true,
            CaptureImages = CaptureImagesBox.IsChecked == true,
            Theme = theme,
            MaskSensitiveData = MaskBox.IsChecked == true,
            HideOnClickOutside = HideOnClickOutsideBox.IsChecked == true,
            SingleClickPaste = SingleClickPasteBox.IsChecked == true,
            ClearClipboardOnDelete = ClearClipboardOnDeleteBox.IsChecked == true,
            AcrylicStrength = (int)Math.Round(AcrylicSlider.Value),
        }.Normalize();

        return true;
    }
}
