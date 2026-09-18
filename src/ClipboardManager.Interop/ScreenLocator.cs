using System.Runtime.InteropServices;

namespace ClipboardManager.Interop;

/// <summary>目标显示器工作区（物理像素，已排除任务栏）。</summary>
/// <param name="Left">工作区左边界（物理像素）。</param>
/// <param name="Top">工作区上边界（物理像素）。</param>
/// <param name="Right">工作区右边界（物理像素）。</param>
/// <param name="Bottom">工作区下边界（物理像素）。</param>
public readonly record struct WorkArea(int Left, int Top, int Right, int Bottom);

/// <summary>
/// 多显示器与 DPI 查询（需求 §5.7）。
/// 面板定位默认落在<b>光标所在显示器</b>的右下角（D-05）。
/// </summary>
public static class ScreenLocator
{
    /// <summary>
    /// 取光标所在显示器的工作区（物理像素）。
    /// </summary>
    /// <param name="dpi">该显示器的 DPI（由调用方通过 <see cref="GetDpiForWindow"/> 提供）。</param>
    /// <param name="workArea">工作区（失败时回退到主屏尺寸）。</param>
    public static bool TryGetCursorWorkArea(out WorkArea workArea)
    {
        workArea = default;

        if (!NativeMethods.GetCursorPos(out var point))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };

        if (!NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        workArea = new WorkArea(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
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
