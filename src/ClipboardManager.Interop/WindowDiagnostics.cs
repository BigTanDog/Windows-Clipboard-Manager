namespace ClipboardManager.Interop;

/// <summary>
/// 窗口诊断助手（只输出类名，不输出标题 —— 标题可能含用户内容，不得进日志）。
/// </summary>
public static class WindowDiagnostics
{
    /// <summary>取窗口类名（失败返回 "?"）。</summary>
    public static string ClassNameOf(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return "<null>";
        }

        var buffer = new char[256];
        var length = NativeMethods.GetClassNameW(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "?";
    }

    /// <summary>把窗口句柄格式化为「0x句柄(类名)」，用于诊断日志。</summary>
    public static string Describe(IntPtr window) =>
        $"0x{window.ToInt64():X}({ClassNameOf(window)})";
}
