namespace ClipboardManager.Core.Theme;

/// <summary>
/// 主题解析（需求 §2：跟随系统深浅色；设置项给 system / light / dark 三档）。
/// <para>
/// 纯函数：把「设置值 + 系统当前是否为浅色」解析成实际生效的主题。
/// 系统取值由调用方从注册表读取（<c>HKCU\...\Themes\Personalize\AppsUseLightTheme</c>），
/// 这样本类不依赖注册表，可完整单测。
/// </para>
/// </summary>
public static class ThemeResolver
{
    /// <summary>浅色主题标识。</summary>
    public const string Light = "light";

    /// <summary>深色主题标识。</summary>
    public const string Dark = "dark";

    /// <summary>跟随系统。</summary>
    public const string System = "system";

    /// <summary>
    /// 解析出实际生效的主题。
    /// </summary>
    /// <param name="setting">设置值（system / light / dark，其它值按 system 处理）。</param>
    /// <param name="systemUsesLightTheme">系统当前是否为浅色（读注册表 AppsUseLightTheme）。</param>
    public static string Resolve(string? setting, bool systemUsesLightTheme)
    {
        var normalized = setting?.Trim().ToLowerInvariant();

        return normalized switch
        {
            Light => Light,
            Dark => Dark,
            _ => systemUsesLightTheme ? Light : Dark,
        };
    }

    /// <summary>主题设置值是否合法。</summary>
    public static bool IsValidSetting(string? setting)
    {
        var normalized = setting?.Trim().ToLowerInvariant();
        return normalized is Light or Dark or System;
    }
}
