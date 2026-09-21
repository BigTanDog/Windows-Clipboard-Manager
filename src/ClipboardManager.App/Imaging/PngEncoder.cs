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

    /// <summary>从磁盘图片文件生成缩略图时的像素上限（4000 万像素）——超过直接放弃，绝不整图解码。</summary>
    public const long ThumbnailSourceMaxPixels = 40_000_000;

    /// <summary>
    /// 从磁盘上的图片文件生成缩略图 PNG（用于"复制的是一张图片文件"的场景）。
    /// <para>
    /// 与 <see cref="TryCreateThumbnail"/> 的两点区别：① <b>先只读文件头拿尺寸</b>，超过
    /// <see cref="ThumbnailSourceMaxPixels"/> 的巨图直接放弃（不把整图读进内存）；
    /// ② <b>始终重新编码为 PNG</b> —— 原图可能是 JPEG/BMP，直接复用会让扩展名与内容不符。
    /// </para>
    /// <para>
    /// 返回 null 表示"生成不了"（格式不支持 / 文件已被移动 / 太大），调用方退化为按普通文件显示即可，
    /// <b>不抛异常</b>。绝不锁定文件（<c>OnLoad</c> + 读完即关闭）。
    /// </para>
    /// </summary>
    /// <param name="path">图片文件绝对路径。</param>
    public static byte[]? TryCreateThumbnailFromFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            int sourceWidth;
            int sourceHeight;

            // 只读文件头拿尺寸（BitmapCacheOption.None 不会整图解码）。
            using (var probe = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.None, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                sourceWidth = frame.PixelWidth;
                sourceHeight = frame.PixelHeight;
            }

            if (sourceWidth <= 0 || sourceHeight <= 0 || (long)sourceWidth * sourceHeight > ThumbnailSourceMaxPixels)
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;

            // 按长边限制设置解码尺寸，避免大图全尺寸解码进内存。
            if (Math.Max(sourceWidth, sourceHeight) > ThumbnailMaxEdge)
            {
                if (sourceWidth >= sourceHeight)
                {
                    image.DecodePixelWidth = ThumbnailMaxEdge;
                }
                else
                {
                    image.DecodePixelHeight = ThumbnailMaxEdge;
                }
            }

            using (var stream = File.OpenRead(path))
            {
                image.StreamSource = stream;
                image.EndInit();
            }

            image.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException
            or IOException or UnauthorizedAccessException)
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
