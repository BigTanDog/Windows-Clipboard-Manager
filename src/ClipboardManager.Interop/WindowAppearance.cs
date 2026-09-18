namespace ClipboardManager.Interop;

/// <summary>
/// 窗口外观增强（DWM）：深色标题栏与圆角。
/// <para>
/// 都是「有则更好」的装饰：Windows 10 上圆角属性会被忽略、深色标题栏需要 1809+，
/// 失败一律静默跳过，绝不因为外观问题影响功能。
/// </para>
/// </summary>
public static class WindowAppearance
{
    /// <summary>属性：标题栏使用深色（Windows 10 1809+）。</summary>
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>属性：窗口圆角偏好（Windows 11）。</summary>
    private const int DwmwaWindowCornerPreference = 33;

    /// <summary>值：圆角。</summary>
    private const int DwmwcpRound = 2;

    /// <summary>
    /// 按当前主题应用窗口外观（深色标题栏 + 圆角）。
    /// </summary>
    /// <param name="hWnd">窗口句柄（必须已创建）。</param>
    /// <param name="dark">是否深色主题。</param>
    public static void ApplyTheme(IntPtr hWnd, bool dark)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        var darkValue = dark ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(hWnd, DwmwaUseImmersiveDarkMode, ref darkValue, sizeof(int));

        var corner = DwmwcpRound;
        _ = NativeMethods.DwmSetWindowAttribute(hWnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
    }
}
