using System.Runtime.InteropServices;
using ClipboardManager.Core.Ui;

namespace ClipboardManager.Interop;

/// <summary>
/// 多显示器与 DPI 查询（需求 §5.7）。
/// 面板定位默认落在<b>光标所在显示器</b>（D-05），并按该屏任务栏所在边贴合（D-05 修订）。
/// </summary>
public static class ScreenLocator
{
    /// <summary>
    /// 取光标所在显示器的整屏矩形与工作区（均为物理像素）。返回 false 表示查询失败，调用方保持原位。
    /// 注意事项：整屏与工作区之差就是任务栏占用的一条边（见 <see cref="PanelPositioning.DetectTaskbarEdge"/>），
    /// 因此两者必须来自<b>同一次</b> <c>GetMonitorInfoW</c> 调用，不能一次取整屏、一次取工作区。
    /// </summary>
    /// <param name="monitor">整屏矩形（rcMonitor）。</param>
    /// <param name="work">工作区（rcWork，已排除任务栏）。</param>
    public static bool TryGetCursorScreen(out ScreenRect monitor, out ScreenRect work)
    {
        monitor = default;
        work = default;

        if (!NativeMethods.GetCursorPos(out var point))
        {
            return false;
        }

        var handle = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };

        if (!NativeMethods.GetMonitorInfoW(handle, ref info))
        {
            return false;
        }

        monitor = new ScreenRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
        work = new ScreenRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
        return true;
    }

    /// <summary>
    /// 取窗口所在显示器的 DPI。注意事项：PerMonitorV2 下每块屏可能不同，
    /// 每次弹出面板都要重新取，不能缓存。
    /// </summary>
    public static uint GetDpiForWindow(IntPtr window)
    {
        var dpi = NativeMethods.GetDpiForWindow(window);
        return dpi == 0 ? 96u : dpi;
    }
}
