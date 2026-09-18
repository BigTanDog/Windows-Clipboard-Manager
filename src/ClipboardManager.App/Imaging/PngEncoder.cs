using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipboardManager.App.Imaging;

/// <summary>
/// 图片编解码（使用 WPF 原生成像栈，不引 GDI+）。
/// <para>
/// 约定：像素格式统一为 <c>Bgra32</c>、<b>自上而下</b>、无行填充，
/// 与 <c>ClipboardManager.Core.Imaging</c> 的 DIB 解析/回写约定一致。
/// </para>
/// </summary>
internal static class PngEncoder
{
    /// <summary>缩略图长边像素上限。</summary>
    public const int ThumbnailMaxEdge = 256;

    /// <summary>BGRA32 像素编码为 PNG。</summary>
    public static byte[] EncodeBgra(int width, int height, byte[] bgraPixels)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);
        var stride = width * 4;
        if (width <= 0 || height <= 0 || (long)stride * height > bgraPixels.Length)
        {
            throw new ArgumentException("像素缓冲与宽高不匹配", nameof(bgraPixels));
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgraPixels, stride);
        source.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>把任意受支持的图片字节解码为 BGRA32 像素（失败返回 false）。</summary>
    public static bool TryDecodeToBgra(byte[] encoded, out int width, out int height, out byte[] bgraPixels)
    {
        width = 0;
        height = 0;
        bgraPixels = [];

        if (encoded is null || encoded.Length == 0)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(encoded, writable: false);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            converted.Freeze();

            width = converted.PixelWidth;
            height = converted.PixelHeight;
            var stride = width * 4;
            var buffer = new byte[stride * height];
            converted.CopyPixels(buffer, stride, 0);
            bgraPixels = buffer;
            return true;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>生成缩略图 PNG（长边不超过 <see cref="ThumbnailMaxEdge"/>）。</summary>
    public static byte[]? TryCreateThumbnail(byte[] encoded)
    {
        if (encoded is null || encoded.Length == 0)
        {
            return null;
        }

        try
        {
            int sourceWidth;
            int sourceHeight;

            // 只读文件头拿尺寸（BitmapDecoder + BitmapCacheOption.None 不会整图解码）。
            using (var probeStream = new MemoryStream(encoded, writable: false))
            {
                var decoder = BitmapDecoder.Create(probeStream, BitmapCreateOptions.None, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                sourceWidth = frame.PixelWidth;
                sourceHeight = frame.PixelHeight;
            }

            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return null;
            }

            if (Math.Max(sourceWidth, sourceHeight) <= ThumbnailMaxEdge)
            {
                return encoded; // 已经足够小，直接复用原图
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;

            // 按长边限制设置解码尺寸，避免大图全尺寸解码进内存。
            if (sourceWidth >= sourceHeight)
            {
                image.DecodePixelWidth = ThumbnailMaxEdge;
            }
            else
            {
                image.DecodePixelHeight = ThumbnailMaxEdge;
            }

            using var stream = new MemoryStream(encoded, writable: false);
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 以「加载后即可关闭文件」的方式读取图片（<c>OnLoad</c> + <c>Freeze</c>）。
    /// 关键：绝不使用默认的文件加载（会锁定文件，导致删除记录时删不掉）。
    /// </summary>
    public static BitmapSource? TryLoadFrozen(string? path, int decodePixelWidth = 0)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0)
            {
                image.DecodePixelWidth = decodePixelWidth;
            }

            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException)
        {
            return null;
        }
    }
}
