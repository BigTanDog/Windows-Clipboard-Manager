using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipboardManager.App;
using ClipboardManager.App.Imaging;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Sensitive;
using ClipboardManager.Core.Text;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 「复制单个图片文件 → 按图片显示（带缩略图）」的测试（用户 2026-09-21 反馈）。
/// <para>
/// 重点约束：<b>只改显示</b> —— 记录类型仍是文件、粘贴出去的仍是文件本身，
/// 而且<b>不缓存原文件内容</b>（"文件内容缓存"是永久非目标），只存一张长边 ≤256px 的缩略图。
/// </para>
/// </summary>
public sealed class ImageFileDisplayTests : IDisposable
{
    private readonly string _directory;

    public ImageFileDisplayTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "clipboard-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 忽略清理失败。
        }
    }

    // ────────────────────── 后缀识别 ──────────────────────

    [Theory]
    [InlineData("a.png")]
    [InlineData("A.PNG")]
    [InlineData("照片.jpg")]
    [InlineData("x.jpeg")]
    [InlineData("y.WebP")]
    [InlineData("z.bmp")]
    [InlineData("t.tiff")]
    [InlineData("i.ico")]
    [InlineData("h.heic")]
    public void 常见图片后缀会被识别(string name)
    {
        Assert.True(ImageFileExtensions.IsImageFile(Path.Combine(_directory, name)));
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("无所谓")]
    [InlineData("a.png.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void 非图片后缀不会被识别(string? name)
    {
        Assert.False(ImageFileExtensions.IsImageFile(name));
    }

    [Fact]
    public void 只识别恰好一个图片文件()
    {
        var png = Path.Combine(_directory, "a.png");
        var txt = Path.Combine(_directory, "a.txt");

        Assert.Equal(png, ImageFileExtensions.TryGetSingleImageFile([png]));
        Assert.Null(ImageFileExtensions.TryGetSingleImageFile([png, png]));   // 多选一堆：不猜
        Assert.Null(ImageFileExtensions.TryGetSingleImageFile([txt]));
        Assert.Null(ImageFileExtensions.TryGetSingleImageFile([]));
    }

    // ────────────────────── 缩略图生成 ──────────────────────

    [Fact]
    public void 从图片文件生成缩略图且长边受限()
    {
        var path = WritePng("big.png", 800, 600);

        var thumbnail = PngEncoder.TryCreateThumbnailFromFile(path);

        Assert.NotNull(thumbnail);
        Assert.True(PngEncoder.TryDecodeToBgra(thumbnail!, out var width, out var height, out _));
        Assert.True(Math.Max(width, height) <= PngEncoder.ThumbnailMaxEdge);
        Assert.True(width > 0 && height > 0);
    }

    [Fact]
    public void JPEG文件生成的缩略图也统一是PNG()
    {
        // 原图是 JPEG：存进本体目录的必须是 PNG（否则扩展名与内容不符，读取端靠内容嗅探只会更脆）。
        var path = Path.Combine(_directory, "photo.jpg");
        var pixels = new byte[8 * 8 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x40;
            pixels[i + 1] = 0x80;
            pixels[i + 2] = 0xC0;
            pixels[i + 3] = 0xFF;
        }

        var source = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, pixels, 8 * 4);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        var thumbnail = PngEncoder.TryCreateThumbnailFromFile(path);

        Assert.NotNull(thumbnail);
        Assert.Equal<byte[]>([0x89, (byte)'P', (byte)'N', (byte)'G'], thumbnail![..4]);
    }

    [Fact]
    public void 生成缩略图不会锁住原文件()
    {
        var path = WritePng("lock.png", 64, 64);
        Assert.NotNull(PngEncoder.TryCreateThumbnailFromFile(path));

        // 锁着文件的话这里会抛 IOException（历史上用 FromFile 加载就踩过这个坑）
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void 非图片与不存在的文件都返回空()
    {
        var text = Path.Combine(_directory, "a.txt");
        File.WriteAllText(text, "这不是图片");

        Assert.Null(PngEncoder.TryCreateThumbnailFromFile(text));
        Assert.Null(PngEncoder.TryCreateThumbnailFromFile(Path.Combine(_directory, "missing.png")));
        Assert.Null(PngEncoder.TryCreateThumbnailFromFile(null));
    }

    // ────────────────────── 捕获处理（本体 = 缩略图） ──────────────────────

    [Fact]
    public void 单个图片文件会把缩略图存为本体()
    {
        var png = WritePng("shot.png", 400, 300);
        var blobs = new BlobStore(_directory);
        var processor = new CaptureProcessor(blobs, new AppLog(null), captureImages: true);

        var processed = processor.Process(FileCandidate(png));

        Assert.NotNull(processed);
        Assert.NotNull(processed!.BlobPath);
        Assert.True(processed.SizeBytes > 0);

        var stored = blobs.TryRead(processed.BlobPath);
        Assert.NotNull(stored);
        Assert.True(PngEncoder.TryDecodeToBgra(stored!, out var width, out var height, out _));
        Assert.True(Math.Max(width, height) <= PngEncoder.ThumbnailMaxEdge);
    }

    [Fact]
    public void 图片文件仍按文件存储且路径与哈希不变()
    {
        // 关键：类型与路径不变 → 粘贴时写回的还是 CF_HDROP（文件），不会被改成"图片内容"；
        // 哈希仍是文件列表哈希 → 去重与"剪贴板中"判定口径不受影响。
        var png = WritePng("keep.png", 64, 64);
        var processor = new CaptureProcessor(new BlobStore(_directory), new AppLog(null), captureImages: true);

        var processed = processor.Process(FileCandidate(png))!;

        Assert.Equal(ClipContentType.FileList, processed.Candidate.Type);
        Assert.Equal<string[]>([png], [.. processed.Candidate.FilePaths]);
        Assert.Equal(ContentHasher.ForFileList([png]), processed.ContentHash);
    }

    [Fact]
    public void 普通文件与多选文件都不生成本体()
    {
        var text = Path.Combine(_directory, "note.txt");
        File.WriteAllText(text, "hello");
        var png = WritePng("b.png", 32, 32);
        var processor = new CaptureProcessor(new BlobStore(_directory), new AppLog(null), captureImages: true);

        Assert.Null(processor.Process(FileCandidate(text))!.BlobPath);
        Assert.Null(processor.Process(FileCandidate(png, text))!.BlobPath);
    }

    // ────────────────────── 展示映射 ──────────────────────

    [Fact]
    public void 单个图片文件有缩略图时按图片显示()
    {
        var item = FileItem(Path.Combine(_directory, "a.png"));

        var withThumbnail = new ClipItemViewModel(
            item,
            SensitiveMasker.Disabled,
            DateTimeOffset.UnixEpoch,
            null,
            Path.Combine(_directory, "thumb.png"));

        Assert.Equal("Image", withThumbnail.KindKey);
        Assert.Equal("图片", withThumbnail.KindLabel);
        Assert.True(withThumbnail.HasThumbnail);

        // 缩略图没生成出来（大图/损坏）时老实显示成文件
        var withoutThumbnail = new ClipItemViewModel(item, SensitiveMasker.Disabled, DateTimeOffset.UnixEpoch);
        Assert.Equal("File", withoutThumbnail.KindKey);
        Assert.Equal("文件", withoutThumbnail.KindLabel);
        Assert.False(withoutThumbnail.HasThumbnail);
    }

    [Fact]
    public void 非图片文件即使给了缩略图路径也按文件显示()
    {
        var item = FileItem(Path.Combine(_directory, "a.txt"));
        var viewModel = new ClipItemViewModel(
            item,
            SensitiveMasker.Disabled,
            DateTimeOffset.UnixEpoch,
            null,
            Path.Combine(_directory, "thumb.png"));

        Assert.Equal("File", viewModel.KindKey);
    }

    // ────────────────────── 夹具 ──────────────────────

    private string WritePng(string name, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x30;
            pixels[i + 1] = 0x60;
            pixels[i + 2] = 0x90;
            pixels[i + 3] = 0xFF;
        }

        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, PngEncoder.EncodeBgra(width, height, pixels));
        return path;
    }

    private static ClipCandidate FileCandidate(params string[] paths) => new()
    {
        Type = ClipContentType.FileList,
        FilePaths = paths,
        CapturedAt = DateTimeOffset.Now,
    };

    private static ClipItem FileItem(string path) => new()
    {
        Id = 1,
        Type = ClipContentType.FileList,
        FilePaths = [path],
        Preview = "1 个文件：" + Path.GetFileName(path),
        ContentHash = "hash",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
