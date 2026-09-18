using System.Runtime.InteropServices;

namespace ClipboardManager.Interop;

/// <summary>
/// 键盘输入注入（粘贴流程的最后一步，需求 §3.4）。
/// </summary>
public static class InputInjector
{
    /// <summary>
    /// 发送一次 Ctrl+V。
    /// <para>
    /// 注意事项：<c>SendInput</c> 受 UIPI 限制 —— 目标窗口若以更高完整性级别运行
    /// （例如以管理员身份启动的程序），注入会被系统丢弃，返回的事件数少于请求数。
    /// 调用方必须检查返回值并走「已复制到剪贴板，请手动粘贴」兜底。
    /// </para>
    /// </summary>
    /// <returns>是否四个事件全部注入成功。</returns>
    public static bool SendCtrlV()
    {
        var inputs = new NativeMethods.INPUT[4];
        inputs[0] = KeyboardEvent(NativeMethods.VK_CONTROL, keyUp: false);
        inputs[1] = KeyboardEvent(NativeMethods.VK_V, keyUp: false);
        inputs[2] = KeyboardEvent(NativeMethods.VK_V, keyUp: true);
        inputs[3] = KeyboardEvent(NativeMethods.VK_CONTROL, keyUp: true);

        var size = Marshal.SizeOf<NativeMethods.INPUT>();
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, size);
        return sent == inputs.Length;
    }

    private static NativeMethods.INPUT KeyboardEvent(ushort virtualKey, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.INPUTUNION
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                wScan = 0,
                dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };
}
