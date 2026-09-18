namespace ClipboardManager.Interop;

/// <summary>
/// 面板窗口的 Win32 样式设置。
/// </summary>
public static class WindowStyling
{
    /// <summary>
    /// 给窗口加上 <c>WS_EX_TOOLWINDOW</c>：不出现在 Alt+Tab（需求 §3.4）。
    /// <para>
    /// 注意事项：必须在窗口句柄创建之后调用（WPF 的 <c>OnSourceInitialized</c>）；
    /// 该样式在窗口显示后设置需要重新显示才完全生效，因此统一提前到 SourceInitialized。
    /// </para>
    /// </summary>
    public static void HideFromAltTab(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtrW(window, NativeMethods.GWL_EXSTYLE).ToInt64();
        var updated = extendedStyle | NativeMethods.WS_EX_TOOLWINDOW;
        if (updated != extendedStyle)
        {
            NativeMethods.SetWindowLongPtrW(window, NativeMethods.GWL_EXSTYLE, new IntPtr(updated));
        }
    }
}
