using System.Buffers.Binary;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipboardManager.App.Imaging;
using ClipboardManager.Core.Imaging;

namespace ClipboardManager.Tests;

/// <summary>
/// 图片链路测试：DIB ↔ BGRA ↔ PNG。
/// <para>
/// 需要 WPF 成像栈（测试工程已开启 <c>UseWPF</c>）。这些用例同时构成「DIB 字节是否被
/// 独立解码器接受」的外部验证 —— 即用 WPF/WIC 解我们写出的 DIB，而不只是自己解析自己。
/// </para>
/// </summary>
public class ImagingTests
{
    [Fact]
    public void PNG编解码往返_像素一致()
    {
        var pixels = BuildTestPixels(4, 3);

        var png = PngEncoder.EncodeBgra(4, 3, pixels);

        Assert.True(PngEncoder.TryDecodeToBgra(png, out var width, out var height, out var decoded));
        Assert.Equal(4, width);
        Assert.Equal(3, height);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void 编码参数非法_抛异常() =>
        Assert.Throws<ArgumentException>(() => PngEncoder.EncodeBgra(4, 3, new byte[8]));

    [Fact]
    public void 解码非法数据_返回false()
    {
        Assert.False(PngEncoder.TryDecodeToBgra([1, 2, 3, 4], out _, out _, out _));
        Assert.False(PngEncoder.TryDecodeToBgra([], out _, out _, out _));
    }

    [Fact]
    public void 缩略图长边不超过上限()
    {
        var pixels = BuildTestPixels(600, 400);
        var png = PngEncoder.EncodeBgra(600, 400, pixels);

        var thumbnail = PngEncoder.TryCreateThumbnail(png);

        Assert.NotNull(thumbnail);
        Assert.True(PngEncoder.TryDecodeToBgra(thumbnail, out var width, out var height, out _));
        Assert.True(Math.Max(width, height) <= PngEncoder.ThumbnailMaxEdge);

        // 宽高比基本保持（缩放后取整会有 1 像素级偏差，所以用容差而不是精确相等）
        Assert.True(
            Math.Abs(((double)width / height) - (600.0 / 400.0)) < 0.02,
            $"宽高比偏差过大：{width}x{height}");
    }

    [Fact]
    public void 小图缩略图直接复用原图()
    {
        var png = PngEncoder.EncodeBgra(32, 32, BuildTestPixels(32, 32));

        var thumbnail = PngEncoder.TryCreateThumbnail(png);

        Assert.NotNull(thumbnail);
        Assert.Equal(png, thumbnail);
    }

    [Fact]
    public void 写出的DIB能被独立解码器识别()
    {
        // 4×2 测试图（自上而下 BGRA）
        var pixels = BuildTestPixels(4, 2);
        var dib = DibWriter.BuildDibV5(4, 2, pixels);

        // 套上 BMP 文件头，让 WPF/WIC 独立解码 —— 这是对 DIB 结构的第三方验证。
        var bmp = WrapInBmpFile(dib);

        using var stream = new MemoryStream(bmp, writable: false);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        Assert.Equal(4, converted.PixelWidth);
        Assert.Equal(2, converted.PixelHeight);

        var decoded = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(decoded, converted.PixelWidth * 4, 0);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void 加载图片不锁定文件()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cm-thumb-{Guid.NewGuid():N}.png");
        var png = PngEncoder.EncodeBgra(8, 8, BuildTestPixels(8, 8));
        File.WriteAllBytes(path, png);

        try
        {
            var image = PngEncoder.TryLoadFrozen(path);
            Assert.NotNull(image);
            Assert.True(image.IsFrozen);

            // 关键：加载后必须能立即删除文件（证明没有持有文件句柄）。
            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void 加载不存在的文件_返回null() =>
        Assert.Null(PngEncoder.TryLoadFrozen(Path.Combine(Path.GetTempPath(), "不存在的文件.png")));

    /// <summary>生成有辨识度的 BGRA 像素（避免全同色掩盖行序错误）。</summary>
    private static byte[] BuildTestPixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = ((y * width) + x) * 4;
                pixels[index] = (byte)(x * 17 + 1);
                pixels[index + 1] = (byte)(y * 23 + 2);
                pixels[index + 2] = (byte)((x + y) * 11 + 3);
                pixels[index + 3] = 255;
            }
        }

        return pixels;
    }

    private static byte[] WrapInBmpFile(byte[] dib)
    {
        var fileHeader = new byte[14];
        fileHeader[0] = (byte)'B';
        fileHeader[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(fileHeader.AsSpan(2), 14 + dib.Length);
        BinaryPrimitives.WriteInt32LittleEndian(fileHeader.AsSpan(10), 14 + DibParser.SizeBitmapV5Header);

        var result = new byte[14 + dib.Length];
        fileHeader.CopyTo(result, 0);
        dib.CopyTo(result, 14);
        return result;
    }
}
