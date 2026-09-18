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

    /// <summary>系统度量索引：小图标宽度（托盘用），96 DPI 下为 16。</summary>
    internal const int SM_CXSMICON = 49;

    /// <summary>窗口样式：弹出式窗口（无边框、无标题栏）。用于创建完全不显示的隐藏消息窗口。</summary>
    internal const uint WS_POPUP = 0x80000000;

    /// <summary>纯文本剪贴板格式（UTF-16，NUL 结尾）。</summary>
    internal const uint CF_UNICODETEXT = 13;

    /// <summary>设备无关位图（无 BMP 文件头，直接是 BITMAPINFOHEADER + 像素）。</summary>
    internal const uint CF_DIB = 8;

    /// <summary>带颜色空间的设备无关位图（BITMAPV5HEADER，支持 alpha）。</summary>
    internal const uint CF_DIBV5 = 17;

    /// <summary>文件路径列表（DROPFILES 结构 + 双 NUL 结尾的路径列表）。</summary>
    internal const uint CF_HDROP = 15;

    /// <summary>DragQueryFileW 的特殊索引：取路径条数。</summary>
    internal const uint DragQueryFileCount = 0xFFFFFFFF;

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

    /// <summary>
    /// 注册（或查询）自定义剪贴板格式。作用：拿到 "HTML Format" 的格式 ID（同名字符串在系统内唯一）。
    /// 注意事项：返回 0 表示失败（内存不足等）；结果应缓存复用，不要每次调用。
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterClipboardFormatW(string lpszFormat);

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

    // ────────────────────── shell32.dll：拖放 / 文件列表 ──────────────────────

    /// <summary>
    /// 从 CF_HDROP 句柄取文件路径。作用：把剪贴板里的文件列表读成字符串。
    /// 注意事项：① 传 <c>iFile = 0xFFFFFFFF</c> 时返回条数，此时 <c>lpszFile</c> 必须为 null；
    /// ② 传索引时返回该路径的长度（字符数，不含结尾 NUL），据此分配缓冲区；
    /// ③ 必须在剪贴板打开期间调用（句柄随关锁失效）。
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, [Out] char[]? lpszFile, uint cch);

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

    // ────────────────────── shell32.dll：托盘图标 ──────────────────────

    /// <summary>
    /// 增删改系统托盘图标。作用：常驻通知区（需求 §3.5）。
    /// 注意事项：① 必须传完整 <c>cbSize</c>；② 删除后 HICON 才能 DestroyIcon；
    /// ③ 资源管理器重启会清空托盘，需要处理 <c>TaskbarCreated</c> 消息后重新 NIM_ADD。
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    /// <summary>创建弹出菜单。作用：托盘右键菜单。注意事项：用完必须 DestroyMenu，否则泄漏 USER 句柄。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr CreatePopupMenu();

    /// <summary>追加菜单项。作用：构造托盘菜单。注意事项：<c>uIDNewItem</c> 传菜单项 id（或子菜单句柄）。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    /// <summary>
    /// 在指定位置弹出菜单（扩展版，无需 TRACKPOPUPMENU 结构）。作用：托盘右键菜单。
    /// 注意事项：① 托盘点击不会激活窗口，弹出前必须 <c>SetForegroundWindow</c>，返回后补一条 <c>WM_NULL</c>，
    /// 否则菜单会"关不掉"；② 用 <c>TPM_RETURNCMD</c> 时返回值即选中的菜单项 id，不需要 WM_COMMAND。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    /// <summary>销毁菜单。见 <see cref="CreatePopupMenu"/>。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(IntPtr hMenu);

    /// <summary>销毁图标句柄。见 <see cref="CreateIconFromResourceEx"/>。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// 由「单个图标图像」数据创建图标句柄（<b>不是</b>整个 .ico 文件；整份传会失败）。
    /// 作用：托盘图标。注意事项：PNG 压缩的图像必须传 <c>dwVer = 0x00030000</c>；
    /// 用完（且在 NIM_DELETE 之后）必须 DestroyIcon，否则句柄泄漏。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr CreateIconFromResourceEx(
        byte[] presbits,
        uint dwResSize,
        [MarshalAs(UnmanagedType.Bool)] bool fIcon,
        uint dwVer,
        int cxDesired,
        int cyDesired,
        uint flags);

    /// <summary>
    /// 在 .ico 文件镜像里按尺寸查找最合适的图像偏移。
    /// 作用：作为 PNG 直传失败时的兜底路径（文档推荐的两步法第一步）。
    /// 注意事项：返回 0 表示没有可用图像；传入的是整份 .ico 数据时返回的是数据偏移。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int LookupIconIdFromDirectoryEx(
        byte[] presbits,
        [MarshalAs(UnmanagedType.Bool)] bool fIcon,
        int cxDesired,
        int cyDesired,
        uint flags);

    /// <summary>取系统度量值。作用：拿托盘小图标的标准边长（<c>SM_CXSMICON</c>），据此选择图标尺寸。</summary>
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);

    /// <summary>
    /// 注册一个全局唯一的窗口消息号（同名返回同一个值）。
    /// 作用：拿到资源管理器广播的 <c>TaskbarCreated</c> 消息号 —— 资源管理器重启后托盘图标会消失，
    /// 收到该消息必须重新添加图标，否则图标永久不见。
    /// 注意事项：返回值 0 表示失败；本消息号是运行时才知道的，不能用常量 switch，需与保存的值比较。
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessageW(string lpString);

    // ────────────────────── dwmapi.dll：窗口外观（圆角 / 深色标题栏） ──────────────────────

    /// <summary>
    /// 设置桌面窗口管理器（DWM）的窗口属性。
    /// 作用：① 让标题栏跟随深色主题（<c>DWMWA_USE_IMMERSIVE_DARK_MODE</c>）
    /// ② 请求圆角窗口（<c>DWMWA_WINDOW_CORNER_PREFERENCE</c>，Windows 11 才有效）。
    /// 注意事项：Windows 10 及更早版本该 API 可能整体失败或忽略未知属性，返回非 0 时静默忽略即可
    /// （外观增强，不影响功能）；属性值必须按 <c>int</c> 传（DWM 只读前 4 字节，传错长度会失败）。
    /// </summary>
    [DllImport("dwmapi.dll", SetLastError = true)]
    internal static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    // ────────────────────── user32.dll：窗口合成（亚克力背景） ──────────────────────

    /// <summary>
    /// 设置窗口合成属性（<b>未公开 API</b>）。作用：给窗口加系统亚克力（模糊 + 染色）背景（附加项 B-04）。
    /// 注意事项：
    /// ① 结构体与常量来自社区逆向，不同 Windows 版本可能失效，调用失败必须静默降级（外观增强不影响功能）；
    /// ② <c>ACCENT_POLICY.GradientColor</c> 是 <b>0xAABBGGRR</b>（ABGR 顺序，不是 ARGB）；
    /// ③ 只有窗口允许逐像素透明（WPF 侧 <c>AllowsTransparency=true</c>）时才看得见模糊，
    /// 否则会被不透明的窗口内容整片盖住；
    /// ④ 数据块必须是非托管内存（本封装在调用方用 <c>Marshal.AllocHGlobal</c> 分配并释放）。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowCompositionAttribute(IntPtr hWnd, ref WINDOWCOMPOSITIONATTRIBDATA data);
}

/// <summary>窗口合成策略（<c>SetWindowCompositionAttribute</c> 的 <c>ACCENT_POLICY</c>）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ACCENTPOLICY
{
    public int AccentState;
    public int AccentFlags;
    public int GradientColor;
    public int AnimationId;
}

/// <summary>窗口合成属性入参（数据块由调用方分配非托管内存，见 <see cref="NativeMethods.SetWindowCompositionAttribute"/>）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWCOMPOSITIONATTRIBDATA
{
    public int Attribute;
    public IntPtr Data;
    public int SizeOfData;
}

/// <summary>
/// 托盘图标数据结构（NOTIFYICONDATAW，Vista+ 完整版）。
/// <para>
/// 字段顺序与自然对齐必须与 Win32 一致（<c>hWnd</c> 前、<c>hIcon</c> 前各有一段隐式 4 字节填充），
/// <c>cbSize</c> 必须等于 <c>Marshal.SizeOf&lt;NOTIFYICONDATAW&gt;()</c>，否则 Shell_NotifyIcon 会直接失败。
/// 定义在命名空间级而非类内，便于 <see cref="TrayIcon"/> 使用。
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NOTIFYICONDATAW
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public IntPtr hIcon;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;

    public uint dwState;
    public uint dwStateMask;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string szInfo;

    /// <summary>超时（老版本）或图标版本（NOTIFYICON_VERSION_4）共用同一块内存。</summary>
    public uint uVersion;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string szInfoTitle;

    public uint dwInfoFlags;
    public Guid guidItem;
    public IntPtr hBalloonIcon;
}
