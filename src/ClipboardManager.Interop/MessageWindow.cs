using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ClipboardManager.Interop;

/// <summary>
/// 仅消息窗口（<c>HWND_MESSAGE</c>）：承载 <c>AddClipboardFormatListener</c> 与全局热键，
/// 7×24 常驻，独立于主 UI 窗口（需求 §3.1）。
/// <para>
/// 线程约束（AGENTS.md §3）：本类必须在 <b>STA</b> 线程上创建（本项目为 WPF UI 线程），
/// 消息经由该线程的消息泵分发，因此所有剪贴板访问天然落在 STA 线程。
/// </para>
/// </summary>
public sealed class MessageWindow : IDisposable
{
    /// <summary>窗口类名（全局唯一，重复注册会失败）。</summary>
    private const string WindowClassName = "ClipboardManager.MessageWindow";

    /// <summary>自定义消息偏移：后台线程请求 STA 线程执行剪贴板写回（见 <see cref="PostToSta"/>）。</summary>
    public const int MessageWriteClipboard = 1;

    /// <summary>自定义消息偏移：托盘图标回调（<c>WM_APP + 2</c>）。</summary>
    public const int MessageTrayCallback = 2;

    /// <summary>资源管理器广播的 TaskbarCreated 消息号（运行时注册，0 表示未注册成功）。</summary>
    private static uint _taskbarCreatedMessage;

    /// <summary>托盘回调消息号（供 <see cref="TrayIcon"/> 注册时使用）。</summary>
    public static uint TrayCallbackMessage => NativeMethods.WM_APP + MessageTrayCallback;

    /// <summary>
    /// 窗口过程委托。必须是 <b>静态字段</b> 持有 —— 委托实例被 GC 回收后，
    /// 系统回调会跳到已释放的地址并直接崩溃进程（Win32 互操作最经典的坑）。
    /// </summary>
    private static readonly WndProcDelegate WndProcCallback = StaticWndProc;

    /// <summary>活跃实例表：把消息路由回具体实例（窗口过程是静态的）。</summary>
    private static readonly Dictionary<IntPtr, MessageWindow> LiveInstances = [];

    private static readonly object ClassGate = new();
    private static ushort _classAtom;

    private IntPtr _handle;
    private bool _disposed;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>剪贴板内容发生变化（WM_CLIPBOARDUPDATE）。</summary>
    public event Action? ClipboardUpdated;

    /// <summary>全局热键被按下（WM_HOTKEY）。</summary>
    public event Action? HotkeyPressed;

    /// <summary>系统设置变化（WM_SETTINGCHANGE，例如深浅色切换）。</summary>
    public event Action? SystemSettingsChanged;

    /// <summary>自定义消息（WM_APP + n，用于把后台线程的请求切回 STA 线程）。</summary>
    public event Action<int>? StaMessageReceived;

    /// <summary>托盘图标回调：事件号（如 WM_LBUTTONUP）、鼠标 x、鼠标 y（物理像素）。</summary>
    public event Action<uint, int, int>? TrayMessage;

    /// <summary>资源管理器（任务栏）重启：必须重新添加托盘图标。</summary>
    public event Action? TaskbarCreated;

    /// <summary>窗口句柄；未创建时为 <see cref="IntPtr.Zero"/>。</summary>
    public IntPtr Handle => _handle;

    /// <summary>创建仅消息窗口。</summary>
    /// <exception cref="Win32Exception">创建失败时抛出，携带 GetLastError 内容。</exception>
    public void Create()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle != IntPtr.Zero)
        {
            return;
        }

        var hInstance = NativeMethods.GetModuleHandleW(null);
        var className = EnsureClassRegistered(hInstance);

        // 注册 TaskbarCreated 消息号（资源管理器重启时广播）：必须在收到消息前拿到，否则会漏掉那次重挂。
        if (_taskbarCreatedMessage == 0)
        {
            _taskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        }

        var handle = NativeMethods.CreateWindowExW(
            dwExStyle: (uint)NativeMethods.WS_EX_TOOLWINDOW, // 常量按 Get/SetWindowLongPtr 的用法声明为 long，这里转回 uint
            lpClassName: className,
            lpWindowName: "ClipboardManager.MessageWindow",
            dwStyle: NativeMethods.WS_POPUP,
            x: 0,
            y: 0,
            nWidth: 0,
            nHeight: 0,
            // 关键决策：这里用「隐藏的顶层窗口」而不是 HWND_MESSAGE 的仅消息窗口。
            // 原因（实测踩坑）：托盘菜单用 TrackPopupMenuEx 弹出，而它要求先把拥有者窗口设为前台
            // （否则菜单会立刻消失）；message-only 窗口无法成为前台窗口，导致菜单根本弹不出来。
            // 该窗口全程不调用 ShowWindow，用户永远看不到，也不会出现在 Alt+Tab（WS_EX_TOOLWINDOW）。
            hWndParent: IntPtr.Zero,
            hMenu: IntPtr.Zero,
            hInstance: hInstance,
            lpParam: IntPtr.Zero);

        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建仅消息窗口失败");
        }

        _handle = handle;
        lock (LiveInstances)
        {
            LiveInstances[handle] = this;
        }
    }

    /// <summary>
    /// 开始接收剪贴板变更通知（<c>AddClipboardFormatListener</c>）。
    /// 作用：内容变化时系统向本窗口发送 WM_CLIPBOARDUPDATE，替代轮询（需求 §3.1）。
    /// 注意事项：必须在窗口创建之后调用；退出时必须与 <see cref="StopClipboardListening"/> 配对，
    /// 否则窗口销毁后系统仍会向悬空句柄投递通知。
    /// </summary>
    /// <param name="error">失败原因。</param>
    public bool TryStartClipboardListening(out string? error)
    {
        error = null;

        if (_handle == IntPtr.Zero)
        {
            error = "消息窗口尚未创建";
            return false;
        }

        if (!NativeMethods.AddClipboardFormatListener(_handle))
        {
            error = $"注册剪贴板监听失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
            return false;
        }

        return true;
    }

    /// <summary>停止接收剪贴板变更通知（幂等，可在未注册时安全调用）。</summary>
    public void StopClipboardListening()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.RemoveClipboardFormatListener(_handle);
        }
    }

    /// <summary>
    /// 从任意线程投递一条自定义消息到本窗口（实际处理发生在 STA 线程）。
    /// 用途：后台线程完成编码后请求 STA 线程写回剪贴板（AGENTS.md §3 禁止后台线程碰剪贴板）。
    /// </summary>
    /// <param name="messageOffset">WM_APP 之上的偏移量。</param>
    public bool PostToSta(int messageOffset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.PostMessageW(
            _handle,
            NativeMethods.WM_APP + (uint)messageOffset,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_handle != IntPtr.Zero)
        {
            lock (LiveInstances)
            {
                LiveInstances.Remove(_handle);
            }

            // 销毁窗口前，上层必须先 RemoveClipboardFormatListener 与 UnregisterHotKey。
            NativeMethods.DestroyWindow(_handle);
            _handle = IntPtr.Zero;
        }
    }

    /// <summary>注册窗口类（进程内只注册一次）。</summary>
    private static string EnsureClassRegistered(IntPtr hInstance)
    {
        lock (ClassGate)
        {
            if (_classAtom != 0)
            {
                return WindowClassName;
            }

            var wc = new NativeMethods.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcCallback),
                hInstance = hInstance,
                lpszClassName = WindowClassName,
            };

            var atom = NativeMethods.RegisterClassExW(ref wc);
            if (atom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                // ERROR_CLASS_ALREADY_EXISTS = 1410：另一个实例已注册同类名，可继续使用。
                if (error != 1410)
                {
                    throw new Win32Exception(error, "注册窗口类失败");
                }
            }

            _classAtom = atom;
            return WindowClassName;
        }
    }

    /// <summary>
    /// 静态窗口过程：按句柄路由到实例，并把异常全部吞掉 —— 异常一旦逃出消息循环会直接终止进程。
    /// </summary>
    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        MessageWindow? instance = null;
        lock (LiveInstances)
        {
            if (msg is NativeMethods.WM_CLIPBOARDUPDATE or NativeMethods.WM_HOTKEY or NativeMethods.WM_SETTINGCHANGE
                || (msg >= NativeMethods.WM_APP && msg < NativeMethods.WM_APP + 0x4000)
                || (msg != 0 && msg == _taskbarCreatedMessage))
            {
                LiveInstances.TryGetValue(hWnd, out instance);
            }
        }

        if (instance is not null)
        {
            try
            {
                switch (msg)
                {
                    case NativeMethods.WM_CLIPBOARDUPDATE:
                        instance.ClipboardUpdated?.Invoke();
                        return IntPtr.Zero;

                    case NativeMethods.WM_HOTKEY:
                        instance.HotkeyPressed?.Invoke();
                        return IntPtr.Zero;

                    case NativeMethods.WM_SETTINGCHANGE:
                        instance.SystemSettingsChanged?.Invoke();
                        return IntPtr.Zero;

                    case var _ when msg == _taskbarCreatedMessage:
                        instance.TaskbarCreated?.Invoke();
                        return IntPtr.Zero;
                }

                if (msg == NativeMethods.WM_APP + MessageTrayCallback)
                {
                    // v4 语义：lParam 低位是事件、高位是图标 id；坐标在 wParam（有符号 16 位打包）。
                    instance.TrayMessage?.Invoke(
                        Core.Ui.TrayNotification.ResolveEvent(lParam),
                        Core.Ui.TrayNotification.ResolveX(wParam),
                        Core.Ui.TrayNotification.ResolveY(wParam));
                    return IntPtr.Zero;
                }

                if (msg >= NativeMethods.WM_APP && msg < NativeMethods.WM_APP + 0x4000)
                {
                    instance.StaMessageReceived?.Invoke((int)(msg - NativeMethods.WM_APP));
                    return IntPtr.Zero;
                }
            }
            catch
            {
                // 明确吞掉：绝不能让异常穿透窗口过程（会直接杀进程）。
                // 各事件订阅方内部自行记录日志。
            }
        }

        return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
    }
}
