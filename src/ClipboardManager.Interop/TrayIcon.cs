using System.Runtime.InteropServices;
using ClipboardManager.Core.Imaging;

namespace ClipboardManager.Interop;

/// <summary>托盘菜单项（纯数据，由调用方构造；<see cref="Separator"/> 表示分隔线）。</summary>
/// <param name="Id">菜单项 id（分隔线忽略）。</param>
/// <param name="Text">显示文字。</param>
/// <param name="Checked">是否显示勾选标记。</param>
/// <param name="Enabled">是否可点击。</param>
/// <param name="IsSeparator">是否为分隔线。</param>
public sealed record TrayMenuItem(int Id, string Text, bool Checked = false, bool Enabled = true, bool IsSeparator = false)
{
    /// <summary>分隔线。</summary>
    public static TrayMenuItem Separator { get; } = new(0, string.Empty, IsSeparator: true);
}

/// <summary>
/// 系统托盘图标（需求 §3.5）：<c>Shell_NotifyIconW</c> + Win32 弹出菜单。
/// <para>
/// 为什么菜单不用 WPF 的 <c>ContextMenu</c>：托盘图标点击<b>不会激活窗口</b>，
/// WPF 菜单依赖窗口激活后的输入路由，会出现「菜单立即消失 / 关不掉」；
/// 用 <c>TrackPopupMenuEx</c> + <c>TPM_RETURNCMD</c> 是 Win32 托盘的标准做法。
/// </para>
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_SHOWTIP = 0x00000080;

    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_CHECKED = 0x00000008;
    private const uint MF_SEPARATOR = 0x00000800;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint WM_NULL = 0x0000;

    private const int TooltipMaxChars = 127; // szTip 为 128 字符（含结尾 NUL）

    private readonly IntPtr _ownerWindow;
    private readonly uint _iconId;
    private readonly uint _callbackMessage;
    private IntPtr _iconHandle;
    private bool _added;
    private bool _disposed;

    /// <summary>创建托盘图标宿主。</summary>
    /// <param name="ownerWindow">接收托盘回调消息的窗口（本项目用隐藏消息窗口，随监听常驻）。</param>
    /// <param name="callbackMessage">回调消息号（建议 WM_APP + N）。</param>
    /// <param name="iconId">图标 id。</param>
    public TrayIcon(IntPtr ownerWindow, uint callbackMessage, uint iconId = 1)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerWindow);
        _ownerWindow = ownerWindow;
        _callbackMessage = callbackMessage;
        _iconId = iconId;
    }

    /// <summary>
    /// 创建（或重建）托盘图标。资源管理器重启后托盘会被清空，必须重新调用本方法。
    /// </summary>
    /// <param name="iconHandle">图标句柄（由 <c>CreateIconFromResourceEx</c> 得到，本类负责最终销毁）。</param>
    /// <param name="tooltip">悬停提示。</param>
    /// <param name="error">失败原因。</param>
    public bool TryAdd(IntPtr iconHandle, string tooltip, out string? error)
    {
        error = null;
        if (iconHandle == IntPtr.Zero)
        {
            error = "图标句柄无效";
            return false;
        }

        _iconHandle = iconHandle;
        var data = CreateData(iconHandle, tooltip);

        if (!NativeMethods.Shell_NotifyIconW(NIM_ADD, ref data))
        {
            error = $"添加托盘图标失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
            return false;
        }

        // 声明使用 v4 语义：回调消息的 lParam 低位是事件、高位是图标 id，坐标在 wParam。
        data.uVersion = NOTIFYICON_VERSION_4;
        if (!NativeMethods.Shell_NotifyIconW(NIM_SETVERSION, ref data))
        {
            // 非致命：老系统上退化为 v0 语义，事件仍能收到（解析方式不同），这里只记录。
            error = $"设置托盘图标版本失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
        }

        _added = true;
        return true;
    }

    /// <summary>
    /// 由运行时生成的 ICO 字节创建图标并加入通知区。
    /// <para>
    /// 生成的 HICON 由本类持有，<see cref="Dispose"/> 时先 <c>NIM_DELETE</c> 再 <c>DestroyIcon</c>。
    /// </para>
    /// </summary>
    /// <param name="icoBytes">ICO 文件字节（见 <c>IcoWriter</c>）。</param>
    /// <param name="tooltip">悬停提示。</param>
    /// <param name="error">失败原因。</param>
    public bool TryAddFromIco(byte[] icoBytes, string tooltip, out string? error)
    {
        ArgumentNullException.ThrowIfNull(icoBytes);
        error = null;

        // 期望尺寸取系统的小图标边长（96 DPI = 16，150% = 24…），据此挑最合适的图像。
        var desired = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        if (desired <= 0)
        {
            desired = 16;
        }

        var handle = CreateIconHandle(icoBytes, desired);
        if (handle == IntPtr.Zero)
        {
            error = $"创建托盘图标句柄失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
            return false;
        }

        if (!TryAdd(handle, tooltip, out error))
        {
            // 未成功入托盘，句柄由本方法负责销毁（TryAdd 只保存不销毁）。
            _ = NativeMethods.DestroyIcon(handle);
            _iconHandle = IntPtr.Zero;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 由 ICO 字节创建 HICON。
    /// <para>
    /// 实测结论（重要）：<c>CreateIconFromResourceEx</c> 要的是「<b>单个图标图像</b>」，
    /// 直接传整份 .ico 文件（含 ICONDIR 目录）会失败（返回 0，且 <c>GetLastError</c> 为 0，极难排查）。
    /// 因此这里先用 <c>IcoWriter.TryGetImage</c> 取出最接近目标尺寸的图像（PNG，dwVer=0x00030000），
    /// 失败时再退回文档推荐的两步法（<c>LookupIconIdFromDirectoryEx</c> + 偏移切片）。
    /// </para>
    /// </summary>
    private static IntPtr CreateIconHandle(byte[] icoBytes, int desired)
    {
        if (IcoWriter.TryGetImage(icoBytes, desired, out var image, out var imageSize))
        {
            var handle = NativeMethods.CreateIconFromResourceEx(
                image,
                (uint)image.Length,
                fIcon: true,
                dwVer: 0x00030000,
                cxDesired: imageSize,
                cyDesired: imageSize,
                flags: 0);

            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }

        // 兜底：按目录查找偏移后再切一段（部分系统对 PNG 直传有额外要求）。
        var offset = NativeMethods.LookupIconIdFromDirectoryEx(icoBytes, fIcon: true, desired, desired, flags: 0);
        if (offset > 0 && offset < icoBytes.Length)
        {
            var slice = icoBytes[offset..];
            return NativeMethods.CreateIconFromResourceEx(
                slice,
                (uint)slice.Length,
                fIcon: true,
                dwVer: 0x00030000,
                cxDesired: desired,
                cyDesired: desired,
                flags: 0);
        }

        return IntPtr.Zero;
    }

    /// <summary>更新悬停提示（图标未创建时为空操作）。</summary>
    /// <param name="tooltip">新提示文本。</param>
    public void UpdateTooltip(string tooltip)
    {
        if (!_added)
        {
            return;
        }

        var data = CreateData(_iconHandle, tooltip);
        data.uFlags = NIF_TIP | NIF_SHOWTIP;
        _ = NativeMethods.Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    /// <summary>移除托盘图标（幂等）。</summary>
    public void Remove()
    {
        if (!_added)
        {
            return;
        }

        var data = CreateData(IntPtr.Zero, string.Empty);
        _ = NativeMethods.Shell_NotifyIconW(NIM_DELETE, ref data);
        _added = false;
    }

    /// <summary>
    /// 在指定屏幕坐标弹出右键菜单，返回用户选择的菜单项 id（未选择返回 0）。
    /// </summary>
    /// <param name="items">菜单项。</param>
    /// <param name="x">屏幕坐标 x（物理像素）。</param>
    /// <param name="y">屏幕坐标 y（物理像素）。</param>
    public int ShowMenu(IReadOnlyList<TrayMenuItem> items, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return 0;
        }

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    _ = NativeMethods.AppendMenuW(menu, MF_SEPARATOR, 0, null);
                    continue;
                }

                var flags = MF_STRING;
                if (item.Checked)
                {
                    flags |= MF_CHECKED;
                }

                if (!item.Enabled)
                {
                    flags |= MF_GRAYED;
                }

                _ = NativeMethods.AppendMenuW(menu, flags, (nuint)item.Id, item.Text);
            }

            // 经典收尾（缺一不可）：托盘点击不激活窗口，必须先抢前台，弹出后再补一条 WM_NULL，
            // 否则菜单会"点不掉"或点到别处不消失。
            _ = NativeMethods.SetForegroundWindow(_ownerWindow);

            var command = NativeMethods.TrackPopupMenuEx(
                menu,
                TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                x,
                y,
                _ownerWindow,
                IntPtr.Zero);

            _ = NativeMethods.PostMessageW(_ownerWindow, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return command;
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menu);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 顺序固定：先从托盘移除，再销毁 HICON（否则托盘里会留下失效图标）。
        Remove();

        if (_iconHandle != IntPtr.Zero)
        {
            _ = NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    private NOTIFYICONDATAW CreateData(IntPtr iconHandle, string tooltip)
    {
        var text = tooltip.Length > TooltipMaxChars ? tooltip[..TooltipMaxChars] : tooltip;

        return new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _ownerWindow,
            uID = _iconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
            uCallbackMessage = _callbackMessage,
            hIcon = iconHandle,
            szTip = text,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
    }
}
