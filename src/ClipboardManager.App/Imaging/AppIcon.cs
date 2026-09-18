using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipboardManager.App.Imaging;

/// <summary>
/// 应用图标（<c>Assets/app.ico</c>，以嵌入资源随程序集分发）。
/// <para>
/// 同一份 ICO 同时供三处使用：exe 文件图标（编译期由 <c>ApplicationIcon</c> 嵌入）、
/// 窗口图标（本类给出 <see cref="ImageSource"/>）、托盘图标（本类给出字节，交给 <c>TrayIcon</c>）。
/// 图标由 <c>scripts/make-icon.ps1</c> 从源图生成，改图标只改源图重跑脚本即可。
/// </para>
/// </summary>
internal static class AppIcon
{
    private const string ResourceName = "ClipboardManager.App.Assets.app.ico";

    private static byte[]? _bytes;
    private static ImageSource? _source;

    /// <summary>读取 ICO 字节（资源缺失时返回 null，调用方回退到运行时绘制）。</summary>
    public static byte[]? TryReadBytes()
    {
        if (_bytes is not null)
        {
            return _bytes;
        }

        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        _bytes = memory.ToArray();
        return _bytes;
    }

    /// <summary>
    /// 取窗口图标用的 <see cref="ImageSource"/>（取最大尺寸的那一帧，交给系统按需缩放）。
    /// 注意事项：必须 <c>OnLoad</c> 加载并 <c>Freeze</c>，否则会持有流并影响跨线程使用。
    /// </summary>
    public static ImageSource? TryLoadImageSource()
    {
        if (_source is not null)
        {
            return _source;
        }

        var bytes = TryReadBytes();
        if (bytes is null)
        {
            return null;
        }

        using var memory = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            return null;
        }

        var frame = decoder.Frames.OrderByDescending(static f => f.PixelWidth).First();
        frame.Freeze();
        _source = frame;
        return _source;
    }
}
