namespace ClipboardManager.Core.Ui;

/// <summary>任务栏在屏幕上的那条边（面板呼出时贴住它）。</summary>
public enum TaskbarEdge
{
    /// <summary>任务栏在下方（Windows 默认）。</summary>
    Bottom,

    /// <summary>任务栏在上方。</summary>
    Top,

    /// <summary>任务栏在左侧。</summary>
    Left,

    /// <summary>任务栏在右侧。</summary>
    Right,
}

/// <summary>屏幕矩形（物理像素）。Left/Top 可为负值（副屏在主屏左侧/上方时）。</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>
/// 面板定位计算（纯函数，可单测）。
/// 目标：面板贴住<b>目标屏任务栏所在那条边</b>，且不越出工作区、不被任务栏遮挡（D-05 修订）。
/// 另一轴沿用既有偏好：上下任务栏→右对齐，左右任务栏→底对齐。
/// </summary>
public static class PanelPositioning
{
    /// <summary>默认边距（DIP）。</summary>
    public const double DefaultMarginDip = 12;

    /// <summary>
    /// 判断目标屏的任务栏在哪条边。
    /// 依据：整屏矩形（rcMonitor）与工作区（rcWork）的差值 —— 任务栏作为 AppBar 会占掉工作区的一条边。
    /// 注意事项：① 任务栏自动隐藏时没有占用区，差值全为 0，按「下方」回退；
    /// ② 其它停靠栏（侧边栏工具）同样占用工作区，会被当作任务栏判断 —— 取占用最宽的一条边作为结果；
    /// ③ 占用相等时按 下 → 上 → 左 → 右 的顺序取，保证结果稳定。
    /// </summary>
    public static TaskbarEdge DetectTaskbarEdge(ScreenRect monitor, ScreenRect work)
    {
        var bottom = monitor.Bottom - work.Bottom;
        var top = work.Top - monitor.Top;
        var left = work.Left - monitor.Left;
        var right = monitor.Right - work.Right;

        var widest = Math.Max(Math.Max(bottom, top), Math.Max(left, right));
        if (widest <= 0)
        {
            return TaskbarEdge.Bottom;
        }

        if (widest == bottom)
        {
            return TaskbarEdge.Bottom;
        }

        if (widest == top)
        {
            return TaskbarEdge.Top;
        }

        return widest == left ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    /// <summary>
    /// 计算面板左上角坐标（WPF 使用的 DIP 单位）。
    /// </summary>
    /// <param name="work">工作区（物理像素，已排除任务栏）。</param>
    /// <param name="edge">任务栏所在边，决定面板哪条边贴住任务栏。</param>
    /// <param name="widthDip">面板宽度（DIP）。</param>
    /// <param name="heightDip">面板高度（DIP）。</param>
    /// <param name="dpi">显示器 DPI（96 为 100%）。</param>
    /// <param name="marginDip">与工作区边缘的间距（DIP）。</param>
    /// <remarks>
    /// 贴合边永远落在任务栏那一侧；夹紧只在「面板比工作区还大」时生效，
    /// 此时优先保住左上角不越界（面板本身尺寸固定，不可能既完整可见又贴边）。
    /// </remarks>
    public static (double Left, double Top) Compute(
        ScreenRect work,
        TaskbarEdge edge,
        double widthDip,
        double heightDip,
        uint dpi,
        double marginDip = DefaultMarginDip)
    {
        var scale = dpi <= 0 ? 1.0 : dpi / 96.0;
        var left = work.Left / scale;
        var top = work.Top / scale;
        var right = work.Right / scale;
        var bottom = work.Bottom / scale;

        var x = edge == TaskbarEdge.Left
            ? left + marginDip
            : right - widthDip - marginDip;

        var y = edge == TaskbarEdge.Top
            ? top + marginDip
            : bottom - heightDip - marginDip;

        // 放得下时夹紧是空操作；放不下时保证面板左上角不越出工作区。
        x = Math.Clamp(x, left, Math.Max(left, right - widthDip));
        y = Math.Clamp(y, top, Math.Max(top, bottom - heightDip));
        return (x, y);
    }
}
