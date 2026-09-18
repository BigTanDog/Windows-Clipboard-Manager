namespace ClipboardManager.Core.Ui;

/// <summary>
/// 托盘回调消息的参数解析（NOTIFYICON_VERSION_4 约定）。
/// <para>
/// 采用 v4 后，回调消息的 <c>lParam</c> <b>低位字</b>是事件（如 <c>WM_RBUTTONUP</c>），
/// <b>高位字</b>是图标 id；而鼠标消息的坐标在 <c>wParam</c> 里按两个 16 位有符号数打包
/// （低位 x、高位 y，且可能为负，多显示器时必须按有符号解析）。
/// 这套位运算很容易写错，因此独立成纯函数并单测。
/// </para>
/// </summary>
public static class TrayNotification
{
    /// <summary>取事件（<c>lParam</c> 低位字）。</summary>
    public static uint ResolveEvent(IntPtr lParam) => (uint)(lParam.ToInt64() & 0xFFFF);

    /// <summary>取图标 id（<c>lParam</c> 高位字）。</summary>
    public static uint ResolveIconId(IntPtr lParam) => (uint)((lParam.ToInt64() >> 16) & 0xFFFF);

    /// <summary>取鼠标 x（<c>wParam</c> 低位字，按有符号解析以支持负坐标的多屏）。</summary>
    public static int ResolveX(IntPtr wParam) => (short)(wParam.ToInt64() & 0xFFFF);

    /// <summary>取鼠标 y（<c>wParam</c> 高位字，按有符号解析）。</summary>
    public static int ResolveY(IntPtr wParam) => (short)((wParam.ToInt64() >> 16) & 0xFFFF);
}
