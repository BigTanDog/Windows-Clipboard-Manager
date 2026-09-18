using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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

    /// <summary>创建设置窗口。</summary>
    /// <param name="current">当前设置（用于回显）。</param>
    public SettingsWindow(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);

        InitializeComponent();

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
        ExcludeByProcessBox.IsChecked = current.ExcludeByProcessName;
        HotkeyBox.Text = current.Hotkey;

        // 开机自启回显以「注册表里的真实状态」为准，避免设置文件与实际不一致时误导用户。
        AutoStartBox.IsChecked = Interop.AutoStartRegistry.IsEnabledWith(
            Core.AutoStart.AutoStartCommand.Build(Storage.AppPaths.ProcessPath ?? string.Empty, current.AutoStartDelaySeconds));
        AutoStartDelayBox.Text = current.AutoStartDelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        foreach (var app in current.ExcludedApps)
        {
            _excludedApps.Add(app);
        }
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
        }.Normalize();

        return true;
    }
}
