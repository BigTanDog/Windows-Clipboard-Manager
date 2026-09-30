using System.IO;
using ClipboardManager.App;
using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Html;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Settings;
using ClipboardManager.Core.Text;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 补齐既有测试未覆盖分支的边界用例（2026-09-30 用覆盖率报告逐一对照后补）。
/// <para>
/// 覆盖对象：日志器（滚动/截断/静默）、设置保存与损坏兜底的失败路径、
/// 内容指纹的空值与畸形输入、捕获处理器的异常/关闭分支、相对时间与 HTML 结构判定的边角。
/// </para>
/// </summary>
public sealed class AppLogTests : IDisposable
{
    private readonly string _directory;

    public AppLogTests()
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

    [Fact]
    public void 内容裁剪到上限字符数()
    {
        Assert.Equal(string.Empty, AppLog.TruncateContent(null));

        var exact = new string('a', AppLog.MaxContentChars);
        Assert.Equal(exact, AppLog.TruncateContent(exact));

        var longer = new string('a', AppLog.MaxContentChars + 10);
        var truncated = AppLog.TruncateContent(longer);
        Assert.Equal(AppLog.MaxContentChars + 1, truncated.Length);
        Assert.EndsWith("…", truncated, StringComparison.Ordinal);
    }

    [Fact]
    public void 诊断日志只在开启时写入文件()
    {
        var quiet = new AppLog(_directory);
        quiet.Info("info-line");
        quiet.Diag("diag-line");

        var content = File.ReadAllText(Path.Combine(_directory, "app.log"));
        Assert.Contains("info-line", content, StringComparison.Ordinal);
        Assert.Contains("INFO ", content, StringComparison.Ordinal);
        Assert.DoesNotContain("diag-line", content, StringComparison.Ordinal);

        var verbose = new AppLog(_directory, verbose: true);
        Assert.True(verbose.Verbose);
        verbose.Diag("diag-line-2");
        Assert.Contains(
            "diag-line-2",
            File.ReadAllText(Path.Combine(_directory, "app.log")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void 错误日志记录异常类型与消息()
    {
        new AppLog(_directory).Error("boom", new InvalidOperationException("bad-state"));

        var content = File.ReadAllText(Path.Combine(_directory, "app.log"));
        Assert.Contains("ERROR", content, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", content, StringComparison.Ordinal);
        Assert.Contains("bad-state", content, StringComparison.Ordinal);
    }

    [Fact]
    public void 日志目录不可用时静默不抛()
    {
        // 目录传 null = 不写文件；任何级别都不能抛异常（日志失败不影响主流程）。
        var log = new AppLog(null);

        log.Info("x");
        log.Diag("y");
        log.Error("z", new Exception("ignored"));

        Assert.False(log.Verbose);
    }

    [Fact]
    public void 超过单文件上限时轮转并顺移最旧文件()
    {
        var logPath = Path.Combine(_directory, "app.log");
        File.WriteAllBytes(logPath, new byte[2 * 1024 * 1024]); // 恰好达到上限
        File.WriteAllText(Path.Combine(_directory, "app.1.log"), "old-1");
        File.WriteAllText(Path.Combine(_directory, "app.2.log"), "old-2");

        new AppLog(_directory).Info("after-roll");

        // app.log 被轮转为 app.1、原 app.1 变 app.2、最旧的 app.2 被丢弃
        Assert.Contains("after-roll", File.ReadAllText(logPath), StringComparison.Ordinal);
        Assert.Equal(2 * 1024 * 1024, new FileInfo(Path.Combine(_directory, "app.1.log")).Length);
        Assert.Equal("old-1", File.ReadAllText(Path.Combine(_directory, "app.2.log")));
    }

    [Fact]
    public void 未达上限时不轮转()
    {
        var logPath = Path.Combine(_directory, "app.log");
        File.WriteAllText(logPath, "small");

        new AppLog(_directory).Info("second");

        Assert.False(File.Exists(Path.Combine(_directory, "app.1.log")));
        var content = File.ReadAllText(logPath);
        Assert.Contains("small", content, StringComparison.Ordinal);
        Assert.Contains("second", content, StringComparison.Ordinal);
    }
}

/// <summary>设置持久化的失败路径（写不进去、坏文件备份也失败）—— 这两条分支都不能把异常抛给调用方。</summary>
public sealed class SettingsStoreFailureTests : IDisposable
{
    private readonly string _directory;

    public SettingsStoreFailureTests()
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

    [Fact]
    public void 目标路径不可写时保存失败并给出原因()
    {
        // 用文件占住"目录"的位置 → Directory.CreateDirectory 必然失败
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "x");

        var store = new SettingsStore(Path.Combine(blocker, "settings.json"));

        Assert.False(store.TrySave(AppSettings.Default, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains("保存设置失败", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void 损坏的设置文件回退默认值且备份失败也不抛()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ 这不是合法 JSON");
        // 备份目标位置被目录占住 → File.Move 失败 → 只能记日志，绝不能抛
        Directory.CreateDirectory(path + ".bad");

        var store = new SettingsStore(path, new AppLog(null));

        Assert.Equal(AppSettings.Default.MaxItems, store.Load().MaxItems);
    }
}

/// <summary>内容指纹的空值与畸形输入分支（与捕获口径的一致性由 ClipboardRevokeTests 的等价性测试守着）。</summary>
public class ClipboardFingerprintEdgeTests
{
    [Fact]
    public void 未知内容类型返回空()
    {
        Assert.Null(ClipboardFingerprint.Of(Candidate((ClipContentType)99)));
    }

    [Fact]
    public void 零字节本体返回空()
    {
        Assert.Null(ClipboardFingerprint.Of(Candidate(ClipContentType.Html, binary: [])));
        Assert.Null(ClipboardFingerprint.Of(Candidate(ClipContentType.Html)));
        Assert.Null(ClipboardFingerprint.Of(Candidate(ClipContentType.Image, binary: [])));
        Assert.Null(ClipboardFingerprint.Of(Candidate(ClipContentType.Image)));
    }

    [Fact]
    public void 文件列表为空或全非法时返回空()
    {
        Assert.Null(ClipboardFingerprint.Of(Candidate(ClipContentType.FileList, paths: [])));
        Assert.Null(ClipboardFingerprint.Of(Candidate(ClipContentType.FileList, paths: ["相对路径.txt"])));
    }

    [Fact]
    public void 空文本仍有指纹()
    {
        Assert.NotNull(ClipboardFingerprint.Of(Candidate(ClipContentType.Text)));
        Assert.NotNull(ClipboardFingerprint.Of(Candidate(ClipContentType.Text, text: string.Empty)));
    }

    private static ClipCandidate Candidate(
        ClipContentType type,
        string? text = null,
        byte[]? binary = null,
        string[]? paths = null) => new()
        {
            Type = type,
            Text = text,
            Binary = binary,
            FilePaths = paths ?? [],
            CapturedAt = DateTimeOffset.Now,
        };
}

/// <summary>捕获处理器的丢弃分支（开关关闭、空本体、解析失败、写盘失败）—— 都必须退化为"不记录"而不是抛异常。</summary>
public sealed class CaptureProcessorBranchTests : IDisposable
{
    private readonly string _directory;

    public CaptureProcessorBranchTests()
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

    [Fact]
    public void 未知内容类型被忽略()
    {
        Assert.Null(Processor().Process(Candidate((ClipContentType)99)));
    }

    [Fact]
    public void 文件列表全非法时忽略_部分非法时保留合法项()
    {
        var text = Path.Combine(_directory, "note.txt");
        File.WriteAllText(text, "hello");

        Assert.Null(Processor().Process(Candidate(ClipContentType.FileList, paths: ["相对路径.txt"])));

        var mixed = Processor().Process(Candidate(ClipContentType.FileList, paths: [text, "相对路径.txt"]));

        Assert.NotNull(mixed);
        Assert.Equal<string[]>([text], [.. mixed!.Candidate.FilePaths]);
    }

    [Fact]
    public void 图片文件缩略图生成失败时退化为普通文件记录()
    {
        // 后缀是 .png 但内容不是图片 → 缩略图生成失败，仍应入库（只是没有缩略图）
        var fake = Path.Combine(_directory, "fake.png");
        File.WriteAllText(fake, "这不是图片");

        var processed = Processor().Process(Candidate(ClipContentType.FileList, paths: [fake]));

        Assert.NotNull(processed);
        Assert.Null(processed!.BlobPath);
    }

    [Fact]
    public void 本体写盘失败时仍入库但不带本体()
    {
        var png = WritePng("ok.png");
        var hash = ContentHasher.ForFileList([png]);

        // 用文件占住 blobs/<哈希前两位> 的位置 → Directory.CreateDirectory 失败
        var blocker = Path.Combine(_directory, "blobs", hash[..2]);
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, "block");

        var processed = Processor().Process(Candidate(ClipContentType.FileList, paths: [png]));

        Assert.NotNull(processed);
        Assert.Null(processed!.BlobPath);
    }

    [Fact]
    public void HTML本体为空或无法解析时忽略()
    {
        Assert.Null(Processor().Process(Candidate(ClipContentType.Html)));
        Assert.Null(Processor().Process(Candidate(ClipContentType.Html, binary: [])));
        Assert.Null(Processor().Process(Candidate(ClipContentType.Html, binary: [1, 2, 3])));
    }

    [Fact]
    public void 关闭记录图片后图片候选被忽略()
    {
        Assert.Null(Processor(captureImages: false)
            .Process(Candidate(ClipContentType.Image, binary: [1, 2, 3])));
    }

    [Fact]
    public void 图片本体为空或无法解析时忽略()
    {
        Assert.Null(Processor().Process(Candidate(ClipContentType.Image)));
        Assert.Null(Processor().Process(Candidate(ClipContentType.Image, binary: [9, 9, 9])));
    }

    [Fact]
    public void 文本候选正常入库()
    {
        var processed = Processor().Process(Candidate(ClipContentType.Text, text: "一段文字"));

        Assert.NotNull(processed);
        Assert.Equal(ContentHasher.ForText("一段文字"), processed!.ContentHash);
        Assert.Null(processed.BlobPath);
    }

    private CaptureProcessor Processor(bool captureImages = true) =>
        new(new BlobStore(_directory), new AppLog(null), captureImages);

    private string WritePng(string name)
    {
        var pixels = new byte[8 * 8 * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0x20;
            pixels[index + 1] = 0x60;
            pixels[index + 2] = 0xA0;
            pixels[index + 3] = 0xFF;
        }

        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, ClipboardManager.App.Imaging.PngEncoder.EncodeBgra(8, 8, pixels));
        return path;
    }

    private static ClipCandidate Candidate(
        ClipContentType type,
        string? text = null,
        byte[]? binary = null,
        string[]? paths = null) => new()
        {
            Type = type,
            Text = text,
            Binary = binary,
            FilePaths = paths ?? [],
            CapturedAt = DateTimeOffset.Now,
        };
}

/// <summary>相对时间与 HTML 结构判定的边角分支。</summary>
public class TextEdgeCaseTests
{
    [Fact]
    public void 更早的日期显示日期与时间()
    {
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(8));

        // 同年、非昨天 → MM-dd HH:mm
        Assert.Equal(
            "09-20 08:05",
            RelativeTime.Format(new DateTimeOffset(2026, 9, 20, 8, 5, 0, TimeSpan.FromHours(8)), now));

        // 往年 → yyyy-MM-dd HH:mm
        Assert.Equal(
            "2025-12-31 23:30",
            RelativeTime.Format(new DateTimeOffset(2025, 12, 31, 23, 30, 0, TimeSpan.FromHours(8)), now));
    }

    [Fact]
    public void 未闭合或空标签名不算结构元素()
    {
        Assert.False(HtmlStructure.HasStructuralElement("<p 未闭合"));
        Assert.False(HtmlStructure.HasStructuralElement("< >文字"));
        Assert.False(HtmlStructure.HasStructuralElement("<-->文字"));
    }

    [Fact]
    public void 带命名空间前缀的标签按本名判定()
    {
        // o:p → p（结构换行）；o:span → span（纯排版样式，不算结构）
        Assert.True(HtmlStructure.HasStructuralElement("<o:p>段落</o:p>"));
        Assert.False(HtmlStructure.HasStructuralElement("<o:span style=\"color:red\">文字</o:span>"));
    }
}
