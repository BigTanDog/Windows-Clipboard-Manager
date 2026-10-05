using ClipboardManager.App.Imaging;

namespace ClipboardManager.Tests;

/// <summary>
/// 底部栏图标的矢量几何（用户 2026-10-05 要求：齿轮要更像齿轮、更醒目）。
/// <para>
/// 图标取自 Material Design Icons（Apache-2.0）的 24×24 path，放在 <see cref="IconGeometry"/> 里。
/// 这里用真实的几何解析兜底：path 写错时面板会<b>静默不显示图标</b>（不抛异常、不留日志），
/// 只有测试才能提前发现。
/// </para>
/// </summary>
public class IconGeometryTests
{
    [Fact]
    public void 齿轮几何可解析且有实际尺寸()
    {
        var gear = IconGeometry.Gear;

        Assert.NotNull(gear);
        Assert.False(gear.IsEmpty());
        Assert.True(gear.Bounds.Width > 0 && gear.Bounds.Height > 0);
    }

    [Fact]
    public void 星形几何可解析且接近正方形()
    {
        var star = IconGeometry.Star;

        Assert.False(star.IsEmpty());
        Assert.InRange(star.Bounds.Width, 18, 24);
        Assert.InRange(star.Bounds.Height, 16, 24);
    }

    [Fact]
    public void 图标几何已冻结以便跨窗口共享()
    {
        // 未冻结的 Freezable 会绑定创建它的线程，在 UI 线程使用会抛异常。
        Assert.True(IconGeometry.Gear.IsFrozen);
        Assert.True(IconGeometry.Star.IsFrozen);
    }
}
