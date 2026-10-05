using System.Windows.Media;

namespace ClipboardManager.App.Imaging;

/// <summary>
/// 界面图标的矢量几何（面板底部按钮用）。
/// <para>
/// 来源：Google <c>Material Design Icons</c>（npm 包 <c>@material-design-icons/svg</c>，Apache-2.0），
/// 取 <c>filled/settings</c>（齿轮）与 <c>filled/star</c>（星形）两个 24×24 图标的原始 path 数据，
/// 用 <b>Fill 填充</b>（早先的齿轮是自绘线框，视觉上像"小太阳"，用户 2026-10-05 反馈要更像齿轮、更醒目）。
/// </para>
/// <para>
/// 为什么把 path 放在 C# 而不是 XAML 资源里：这样「这些 path 能被 WPF 解析」有单元测试兜底
/// （<c>IconGeometryTests</c>）—— path 写错时面板会直接不显示图标，而 XAML 字符串错误很难在测试里发现。
/// </para>
/// </summary>
public static class IconGeometry
{
    /// <summary>齿轮（右下角「设置」按钮）。</summary>
    public static Geometry Gear { get; } = Create(
        "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58a.49.49 0 0 0 .12-.61"
        + "l-1.92-3.32a.488.488 0 0 0-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54"
        + "a.484.484 0 0 0-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94"
        + "l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58"
        + "c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58a.49.49 0 0 0-.12.61l1.92 3.32"
        + "c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84"
        + "c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22"
        + "l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58z"
        + "M12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z");

    /// <summary>实心星形（「只看收藏」筛选按钮）。</summary>
    public static Geometry Star { get; } = Create(
        "M12 17.27 18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z");

    /// <summary>
    /// 解析并<b>冻结</b>几何：冻结后才能在多个窗口（/线程）之间安全共享 —— 未冻结的
    /// <see cref="Freezable"/> 会绑定到创建它的线程，跨线程使用会抛异常。
    /// </summary>
    private static Geometry Create(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
