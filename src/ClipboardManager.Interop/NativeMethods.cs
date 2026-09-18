using System.Runtime.InteropServices;

namespace ClipboardManager.Interop;

/// <summary>
/// 全部 Win32 互操作声明的唯一入口（AGENTS.md §6：Win32 代码必须注释作用与调用注意事项）。
/// <para>
/// 说明：技术设计 §2.1 原计划用 CsWin32 生成签名；实施时改为手写（偏差已记录在
/// <c>ClipboardManager.Interop.csproj</c> 与本文件注释中），原因是 AGENTS.md 要求
/// 「涉及 Win32 API 的代码都要注释说明作用与调用注意事项」，生成代码无法承载注释。
/// </para>
/// <para>
/// 通用约定：所有字符串 API 一律使用 <c>W</c>（UTF-16）版本；所有返回 BOOL 的调用
/// 都标注 <see cref="MarshalAsAttribute"/>，避免默认 4 字节 int 造成的返回值误判。
/// </para>
/// </summary>
internal static class NativeMethods
{
    // ────────────────────────────── 常量 ──────────────────────────────

    /// <summary>剪贴板内容变化通知（AddClipboardFormatListener 注册后由系统发送）。</summary>
    internal const uint WM_CLIPBOARDUPDATE = 0x031D;

    /// <summary>热键被按下（RegisterHotKey 注册成功后由系统发送）。</summary>
    internal const uint WM_HOTKEY = 0x0312;

    /// <summary>系统设置变化（用于深浅色跟随）。</summary>
    internal const uint WM_SETTINGCHANGE = 0x001A;

    /// <summary>自定义消息起点：WM_APP + n，用于后台线程请求 STA 线程执行剪贴板写回。</summary>
    internal const uint WM_APP = 0x8000;

    /// <summary>纯文本剪贴板格式（UTF-16，NUL 结尾）。</summary>
    internal const uint CF_UNICODETEXT = 13;

    /// <summary>GlobalAlloc：可移动内存块（剪贴板要求）。</summary>
    internal const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>HWND_MESSAGE：仅消息窗口的父窗口句柄，此类窗口不可见、不进 Alt+Tab。</summary>
    internal static readonly IntPtr HWND_MESSAGE = new(-3);

    /// <summary>SetWindowLongPtr 索引：窗口扩展样式。</summary>
    internal const int GWL_EXSTYLE = -20;

    /// <summary>WS_EX_TOOLWINDOW：不在 Alt+Tab 与任务栏中出现。</summary>
    internal const long WS_EX_TOOLWINDOW = 0x00000080L;

    /// <summary>MOD_NOREPEAT：长按热键不重复触发。</summary>
    internal const uint MOD_NOREPEAT = 0x4000;

    /// <summary>ERROR_ACCESS_DENIED：其它程序正持有剪贴板。</summary>
    internal const int ERROR_ACCESS_DENIED = 5;

    /// <summary>ERROR_CLIPBOARD_NOT_OPEN：剪贴板未打开。</summary>
    internal const int ERROR_CLIPBOARD_NOT_OPEN = 1418;

    /// <summary>MONITOR_DEFAULTTONEAREST：取距离指定点最近的显示器。</summary>
    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>SendInput：键盘输入事件类型。</summary>
    internal const uint INPUT_KEYBOARD = 1;

    /// <summary>SendInput：按键抬起标志。</summary>
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>虚拟键：Ctrl。</summary>
    internal const ushort VK_CONTROL = 0x11;

    /// <summary>虚拟键：V。</summary>
    internal const ushort VK_V = 0x56;

    /// <summary>OpenProcess：只查询有限信息（避免要求更高权限）。</summary>
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // ────────────────────────────── 结构体 ──────────────────────────────

    /// <summary>
    /// 窗口类定义（RegisterClassExW 入参）。
    /// <b>注意</b>：必须包含最后一个成员 <c>hIconSm</c>，否则 <c>cbSize</c> 会比系统期望的
    /// <c>sizeof(WNDCLASSEX)</c>（x64 为 80 字节）少 8 字节，RegisterClassExW 直接返回
    /// ERROR_INVALID_PARAMETER(87)。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    /// <summary>屏幕坐标点。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>矩形（MONITORINFO.rcMonitor / rcWork）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>显示器信息。调用前必须设置 cbSize。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>SendInput 的键盘事件。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>SendInput 的鼠标事件（本工具不发送鼠标事件，仅用于凑齐 INPUT 联合体大小）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>SendInput 的硬件事件（同上，仅为联合体大小）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    /// <summary>
    /// SendInput 的 INPUT 联合体。
    /// <b>关键</b>：必须包含 MOUSEINPUT，否则 <c>sizeof(INPUT)</c> 会小于系统期望值，
    /// SendInput 会直接返回 0 并置 ERROR_INVALID_PARAMETER（这是最容易踩的坑）。
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    /// <summary>SendInput 的输入事件。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    // ────────────────────── user32.dll：窗口与消息 ──────────────────────

    /// <summary>注册窗口类。作用：把窗口过程与类名绑定，消息窗口据此创建。注意事项：类名全局唯一，重复注册返回 0 且 GetLastError=ERROR_CLASS_ALREADY_EXISTS。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    /// <summary>创建窗口。作用：创建仅消息窗口（hWndParent = HWND_MESSAGE）。注意事项：返回值可能为 0（失败），必须检查并用 Marshal.GetLastWin32Error 取错误码。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    /// <summary>销毁窗口。作用：退出时释放消息窗口。注意事项：销毁前必须先 RemoveClipboardFormatListener，否则系统仍会向已销毁的窗口投递通知。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    /// <summary>默认窗口过程。作用：把本进程不处理的消息交回系统。注意事项：不能对已处理的消息调用，否则会出现重复处理。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>读取/设置窗口样式。作用：给面板窗口加 WS_EX_TOOLWINDOW（不进 Alt+Tab）。注意事项：仅 64 位 user32 导出该函数，本工程固定 win-x64（见 Directory.Build.props）。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    /// <summary>见 <see cref="GetWindowLongPtrW"/>。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>投递消息（异步）。作用：后台线程请求 STA 线程执行剪贴板写回（WM_APP + n）。注意事项：只投递、不等待，因此接收方必须自行取数据。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>取当前模块句柄。作用：创建窗口类时需要 hInstance。</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandleW(string? lpModuleName);

    // ────────────────────── user32.dll：剪贴板 ──────────────────────

    /// <summary>注册剪贴板变更监听。作用：内容变化时系统向该窗口发送 WM_CLIPBOARDUPDATE（替代轮询）。注意事项：必须与 RemoveClipboardFormatListener 配对；重复注册同一窗口会失败。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AddClipboardFormatListener(IntPtr hWnd);

    /// <summary>注销剪贴板变更监听。作用：退出前解绑，避免悬空通知。注意事项：窗口销毁前调用。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

    /// <summary>打开剪贴板（全局独占）。作用：读/写剪贴板前必须调用。注意事项：其它程序可能正持有锁，失败时 GetLastError 返回 ERROR_ACCESS_DENIED，需要重试；成功后必须 CloseClipboard，且锁内不得做耗时操作。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenClipboard(IntPtr hWndNewOwner);

    /// <summary>关闭剪贴板。作用：释放全局锁。注意事项：必须与 OpenClipboard 严格配对，且要在任何编码/IO/UI 之前调用。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseClipboard();

    /// <summary>判断剪贴板是否包含指定格式。作用：格式探测。注意事项：需先 OpenClipboard。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsClipboardFormatAvailable(uint format);

    /// <summary>取剪贴板数据句柄。作用：读取指定格式内容。注意事项：返回的 HGLOBAL 归剪贴板所有，<b>不得释放</b>；需先 OpenClipboard；取到后要 GlobalLock 才能读。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetClipboardData(uint uFormat);

    /// <summary>写入剪贴板数据。作用：把内容放到剪贴板。注意事项：<b>成功后内存所有权移交系统，不得再 GlobalFree</b>；失败时所有权仍在自己，必须释放；需先 OpenClipboard + EmptyClipboard。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    /// <summary>清空剪贴板。作用：写入前清掉旧内容。注意事项：会触发一次剪贴板变更通知。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyClipboard();

    /// <summary>取剪贴板序列号。作用：自循环过滤（写入后记录新序列号，收到同一序列号即忽略）。注意事项：无需打开剪贴板，可在锁外安全调用。</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();

    /// <summary>取剪贴板所有者窗口。作用：识别内容来源进程（用于排除应用）。注意事项：很多程序写入后立即销毁窗口，结果可能为 0，必须容错。</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetClipboardOwner();

    /// <summary>取窗口所属线程与进程 ID。作用：由剪贴板所有者窗口反查来源进程。注意事项：需配合 OpenProcess + QueryFullProcessImageName 才能拿到进程名。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // ────────────────────── kernel32.dll：全局内存 ──────────────────────

    /// <summary>分配全局内存。作用：为剪贴板内容准备 HGLOBAL（必须 GMEM_MOVEABLE）。注意事项：返回 0 表示失败；失败路径必须自行释放，不要泄漏。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    /// <summary>锁定全局内存取指针。作用：读写 HGLOBAL 内容。注意事项：用完必须 GlobalUnlock；剪贴板关闭前指针失效。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GlobalLock(IntPtr hMem);

    /// <summary>解锁全局内存。见 <see cref="GlobalLock"/>。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalUnlock(IntPtr hMem);

    /// <summary>释放全局内存。作用：失败路径清理。注意事项：已交给 SetClipboardData 且成功的内存 <b>不可</b> 再释放。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GlobalFree(IntPtr hMem);

    /// <summary>取全局内存大小。作用：为读取剪贴板文本设定长度上限，防止畸形数据无限读取。注意事项：结果以字节为单位。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nuint GlobalSize(IntPtr hMem);

    /// <summary>打开进程取句柄。作用：查询剪贴板来源进程的可执行路径。注意事项：用完必须 CloseHandle；权限不足时返回 0，属正常情况（容错即可）。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    /// <summary>关闭句柄。作用：释放 OpenProcess 取得的句柄。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    /// <summary>取进程映像路径。作用：得到来源进程名。注意事项：先设置缓冲区字符数容量，超出会被截断（返回 FALSE 且 ERROR_INSUFFICIENT_BUFFER）。</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageNameW(
        IntPtr hProcess,
        uint dwFlags,
        [Out] char[] lpExeName,
        ref uint lpdwSize);

    // ────────────────────── user32.dll：热键 / 前台窗口 / 输入 ──────────────────────

    /// <summary>注册全局热键。作用：任意位置唤出面板。注意事项：组合被其它程序占用会失败（返回 FALSE），必须检测并提示用户换键；退出时必须 UnregisterHotKey。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    /// <summary>注销全局热键。见 <see cref="RegisterHotKey"/>。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>取当前前台窗口。作用：面板弹出前记录目标窗口，粘贴时恢复。注意事项：结果可能为 0（无前台窗口）。</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    /// <summary>设置前台窗口。作用：粘贴前把焦点交还给先前窗口。注意事项：受前台锁限制可能失败；调用方必须检查返回值并走兜底提示。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>判断窗口句柄是否有效。作用：目标窗口可能已被关闭，恢复前必须校验，否则会抛异常或静默失败。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);

    /// <summary>取窗口类名。作用：诊断「Ctrl+V 到底注入到了哪个窗口」。注意事项：只取类名，不取标题，避免把用户内容写进日志。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassNameW(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    /// <summary>注入输入事件。作用：发送 Ctrl+V 完成粘贴。注意事项：UIPI 会阻止向更高完整性级别（以管理员运行）的窗口注入，返回的事件数会少于请求数，必须自行判断并兜底。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    // ────────────────────── user32.dll：显示器与 DPI ──────────────────────

    /// <summary>取鼠标位置（物理像素）。作用：确定面板出现在哪块屏。注意事项：多屏坐标系可能含负值。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>按点取显示器句柄。作用：多屏定位。注意事项：点不在任何显示器上时返回最近的一个（配合 MONITOR_DEFAULTTONEAREST）。</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    /// <summary>取显示器信息。作用：拿到工作区（已排除任务栏）用于右下角定位。注意事项：调用前必须设置 cbSize，否则返回 FALSE。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>取窗口所在显示器的 DPI。作用：物理像素与 WPF 的 DIP 换算。注意事项：PerMonitorV2 下每块屏可能不同，必须每次弹出重新计算。</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hWnd);
}
