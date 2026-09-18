namespace ClipboardManager.Core.Ui;

/// <summary>
/// 面板定位计算（纯函数，可单测）。
/// 目标：把面板放在指定显示器工作区的右下角，且与任务栏保持间距（D-05，对齐 Win+V 观感）。
/// </summary>
public static class PanelPositioning
{
    /// <summary>默认边距（DIP）。</summary>
    public const double DefaultMarginDip = 12;

    /// <summary>
    /// 计算面板左上角坐标（WPF 使用的 DIP 单位）。
    /// </summary>
    /// <param name="workRight">工作区右边界（物理像素）。</param>
    /// <param name="workBottom">工作区下边界（物理像素）。</param>
    /// <param name="widthDip">面板宽度（DIP）。</param>
    /// <param name="heightDip">面板高度（DIP）。</param>
    /// <param name="dpi">显示器 DPI（96 为 100%）。</param>
    /// <param name="marginDip">距工作区边缘的间距（DIP）。</param>
    public static (double Left, double Top) ComputeBottomRight(
        int workRight,
        int workBottom,
        double widthDip,
        double heightDip,
        uint dpi,
        double marginDip = DefaultMarginDip)
    {
        var scale = dpi <= 0 ? 1.0 : dpi / 96.0;
        var left = (workRight / scale) - widthDip - marginDip;
        var top = (workBottom / scale) - heightDip - marginDip;
        return (left, top);
    }
}
