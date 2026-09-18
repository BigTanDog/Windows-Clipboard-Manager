using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipboardManager.Core.Imaging;

namespace ClipboardManager.App.Imaging;

/// <summary>
/// 运行时绘制托盘图标（避免在仓库里放二进制资源）。
/// <para>
/// 画出 ICO 字节后交给 <c>CreateIconFromResourceEx</c> 得到 HICON。
/// 16 与 32 两个尺寸都要给 —— 高 DPI 与不同缩放比例下系统会取不同尺寸。
/// </para>
/// </summary>
internal static class TrayIconImage
{
    /// <summary>需要生成的尺寸（像素）。</summary>
    private static readonly int[] Sizes = [16, 32];

    /// <summary>
    /// 生成托盘图标的 ICO 字节。
    /// </summary>
    public static byte[] BuildIco()
    {
        var images = new List<IcoWriter.IcoImage>(Sizes.Length);
        foreach (var size in Sizes)
        {
            images.Add(new IcoWriter.IcoImage(size, PngEncoder.EncodeBgra(size, size, RenderClipboardGlyph(size))));
        }

        return IcoWriter.Build(images);
    }

    /// <summary>在 size×size 的画布上绘制「剪贴板」图标，输出自上而下的 BGRA32 像素。</summary>
    private static byte[] RenderClipboardGlyph(int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var scale = size / 32.0;

            // 画板主体（深色描边 + 浅色填充），按 32×32 设计再等比缩放
            var body = new Rect(6 * scale, 4 * scale, 20 * scale, 26 * scale);
            var bodyBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0xF5, 0xEE));
            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x26, 0x21)), Math.Max(1.0, 1.6 * scale));

            context.DrawRoundedRectangle(bodyBrush, borderPen, body, 2.5 * scale, 2.5 * scale);

            // 顶部夹子
            var clip = new Rect(11 * scale, 1.5 * scale, 10 * scale, 5 * scale);
            context.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED)),
                null,
                clip,
                1.5 * scale,
                1.5 * scale);

            // 三条内容线（最下面一条短一些，视觉上像列表）
            var linePen = new Pen(new SolidColorBrush(Color.FromRgb(0x6F, 0x65, 0x58)), Math.Max(1.0, 1.8 * scale));
            for (var index = 0; index < 3; index++)
            {
                var y = (12 + (index * 5)) * scale;
                var width = (index == 2 ? 8 : 12) * scale;
                context.DrawLine(linePen, new Point(10 * scale, y), new Point((10 * scale) + width, y));
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        // Pbgra32 → Bgra32（未预乘）以便统一走 PngEncoder
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        converted.Freeze();

        var buffer = new byte[size * size * 4];
        converted.CopyPixels(buffer, size * 4, 0);
        return buffer;
    }
}
