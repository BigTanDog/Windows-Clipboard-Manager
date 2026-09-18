namespace ClipboardManager.Interop;

/// <summary>
/// 前台窗口的记录与恢复（粘贴流程，需求 §3.4）。
/// 面板会激活自己以接收键盘输入（D-03），因此必须在弹出<b>之前</b>记录目标窗口。
/// </summary>
public static class ForegroundWindowService
{
    /// <summary>记录当前前台窗口（面板弹出前调用）。可能返回 <see cref="IntPtr.Zero"/>。</summary>
    public static IntPtr Capture() => NativeMethods.GetForegroundWindow();

    /// <summary>当前前台窗口句柄（用于注入前校验，避免把 Ctrl+V 打到别的程序里）。</summary>
    public static IntPtr Current => NativeMethods.GetForegroundWindow();

    /// <summary>判断指定窗口是否已是前台窗口。</summary>
    public static bool IsForeground(IntPtr window) =>
        window != IntPtr.Zero && NativeMethods.GetForegroundWindow() == window;

    /// <summary>
    /// 把焦点交还给目标窗口。
    /// 注意事项：受 Windows 前台锁限制，调用可能失败；目标窗口也可能已被销毁，
    /// 因此必须先 <c>IsWindow</c> 校验，失败时由调用方走兜底提示。
    /// </summary>
    public static bool TryRestore(IntPtr window)
    {
        if (window == IntPtr.Zero || !NativeMethods.IsWindow(window))
        {
            return false;
        }

        return NativeMethods.SetForegroundWindow(window);
    }
}
