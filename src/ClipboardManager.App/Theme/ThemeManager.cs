using System.IO;
using System.Windows;
using ClipboardManager.Core.Theme;
using Microsoft.Win32;

namespace ClipboardManager.App.Theme;

/// <summary>
/// 主题应用（需求 §2：跟随系统深浅色；D-13 之外唯一涉及外观的设置）。
/// <para>
/// 实现方式：把 App 资源里合并的主题字典整体换成 <c>Themes/Light.xaml</c> 或
/// <c>Themes/Dark.xaml</c>。控件全部用 <c>DynamicResource</c> 引用颜色键，
/// 因此替换后已打开的窗口会立即变色。
/// </para>
/// </summary>
internal static class ThemeManager
{
    private const string LightSource = "Themes/Light.xaml";
    private const string DarkSource = "Themes/Dark.xaml";

    /// <summary>当前生效的主题（light / dark）。</summary>
    public static string Current { get; private set; } = ThemeResolver.Light;

    /// <summary>读注册表判断系统当前是否为浅色主题；读取失败按浅色处理。</summary>
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);

            // 该值由系统写入：1 = 浅色，0 = 深色
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    /// <summary>按设置值（system/light/dark）解析出实际主题并应用。</summary>
    /// <param name="setting">设置里的主题值。</param>
    /// <returns>实际生效的主题。</returns>
    public static string ApplyFromSetting(string? setting)
    {
        var resolved = ThemeResolver.Resolve(setting, SystemUsesLightTheme());
        Apply(resolved);
        return resolved;
    }

    /// <summary>应用指定主题（light / dark）。</summary>
    /// <param name="theme">主题标识。</param>
    public static void Apply(string theme)
    {
        var normalized = string.Equals(theme, ThemeResolver.Dark, StringComparison.OrdinalIgnoreCase)
            ? ThemeResolver.Dark
            : ThemeResolver.Light;

        var application = Application.Current;
        if (application is null)
        {
            Current = normalized;
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary
        {
            Source = new Uri(normalized == ThemeResolver.Dark ? DarkSource : LightSource, UriKind.Relative),
        };

        // 只替换「主题字典」，不动其它合并进来的资源（例如将来可能的控件库字典）。
        var index = -1;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (source is not null
                && (source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)
                    || source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            dictionaries[index] = replacement;
        }
        else
        {
            dictionaries.Insert(0, replacement);
        }

        Current = normalized;
    }
}
