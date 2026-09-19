using System.IO;
using ClipboardManager.App;
using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Html;
using ClipboardManager.Core.Imaging;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Settings;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 「删除即吊销」相关测试：剪贴板身份记账、内容指纹与捕获路径的同口径保证、设置项默认值与持久化。
/// <para>
/// 其中「指纹 == 入库去重键」这几条是<b>关键回归测试</b>：两处实现一旦漂移，
/// 吊销功能会静默失效（删除后剪贴板没被清空，且没有任何报错），只有这里能拦住。
/// </para>
/// </summary>
public sealed class ClipboardRevokeTests : IDisposable
{
    private readonly string _directory;

    public ClipboardRevokeTests()
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

    // ────────────────────── 身份记账 ──────────────────────

    [Fact]
    public void 未登记时没有任何条目是当前剪贴板内容()
    {
        var tracker = new ClipboardPresenceTracker();

        Assert.False(tracker.IsCurrent(1, 100));
        Assert.False(tracker.HoldsTrackedItem(100));
        Assert.Null(tracker.ItemId);
    }

    [Fact]
    public void 登记后只有同一序列号才算当前()
    {
        var tracker = new ClipboardPresenceTracker();
        tracker.NoteCaptured(100, 42);

        Assert.True(tracker.IsCurrent(42, 100));
        Assert.False(tracker.IsCurrent(43, 100));   // 别的条目
        Assert.False(tracker.IsCurrent(42, 101));   // 序列号变了 = 剪贴板已被改写
        Assert.True(tracker.HoldsTrackedItem(100));
        Assert.False(tracker.HoldsTrackedItem(101));
    }

    [Fact]
    public void 重新登记会覆盖上一条记录()
    {
        var tracker = new ClipboardPresenceTracker();
        tracker.NoteCaptured(100, 42);
        tracker.NoteCaptured(101, 43);

        Assert.False(tracker.IsCurrent(42, 101));
        Assert.True(tracker.IsCurrent(43, 101));
        Assert.Equal(43, tracker.ItemId);
    }

    [Fact]
    public void 复位后不再跟踪任何条目()
    {
        var tracker = new ClipboardPresenceTracker();
        tracker.NoteCaptured(100, 42);
        tracker.Reset();

        Assert.False(tracker.IsCurrent(42, 100));
        Assert.False(tracker.HoldsTrackedItem(100));
    }

    // ────────────────────── 指纹口径一致性（与捕获路径交叉验证） ──────────────────────

    [Fact]
    public void 文本指纹与入库哈希一致()
    {
        var candidate = TextCandidate("hello 剪贴板\r\n第二行");

        Assert.Equal(ProcessHash(candidate), ClipboardFingerprint.Of(candidate));
    }

    [Fact]
    public void 文件列表指纹与入库哈希一致()
    {
        var candidate = new ClipCandidate
        {
            Type = ClipContentType.FileList,
            FilePaths = [Path.Combine(_directory, "a.txt"), Path.Combine(_directory, "b.txt")],
            CapturedAt = DateTimeOffset.Now,
        };

        Assert.Equal(ProcessHash(candidate), ClipboardFingerprint.Of(candidate));
    }

    [Fact]
    public void HTML指纹与入库哈希一致()
    {
        var candidate = new ClipCandidate
        {
            Type = ClipContentType.Html,
            Binary = HtmlClipboardWriter.Build("<b>加粗</b> 正文"),
            BlobExtension = "html",
            CapturedAt = DateTimeOffset.Now,
        };

        Assert.Equal(ProcessHash(candidate), ClipboardFingerprint.Of(candidate));
    }

    [Fact]
    public void 图片指纹与入库哈希一致()
    {
        var bgra = new byte[4 * 4 * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 0x20;
            bgra[i + 1] = 0x80;
            bgra[i + 2] = 0xE0;
            bgra[i + 3] = 0xFF;
        }

        var candidate = new ClipCandidate
        {
            Type = ClipContentType.Image,
            Binary = DibWriter.BuildDibV5(4, 4, bgra),
            BlobExtension = "png",
            CapturedAt = DateTimeOffset.Now,
        };

        Assert.Equal(ProcessHash(candidate), ClipboardFingerprint.Of(candidate));
    }

    [Fact]
    public void 无法解析的候选算不出指纹()
    {
        // 空文件列表：捕获路径会丢弃它，指纹也必须给不出结论（不能瞎认身份）。
        var candidate = new ClipCandidate
        {
            Type = ClipContentType.FileList,
            CapturedAt = DateTimeOffset.Now,
        };

        Assert.Null(ClipboardFingerprint.Of(candidate));
    }

    /// <summary>用真实的捕获处理器算出「入库去重键」，作为指纹口径的基准。</summary>
    private string ProcessHash(ClipCandidate candidate)
    {
        var processor = new CaptureProcessor(new BlobStore(_directory), new AppLog(null), captureImages: true);
        var processed = processor.Process(candidate);

        Assert.NotNull(processed);
        return processed!.ContentHash;
    }

    private static ClipCandidate TextCandidate(string text) => new()
    {
        Type = ClipContentType.Text,
        Text = text,
        CapturedAt = DateTimeOffset.Now,
    };

    // ────────────────────── 设置项 ──────────────────────

    [Fact]
    public void 删除时清空剪贴板默认开启()
    {
        Assert.True(AppSettings.Default.ClearClipboardOnDelete);
    }

    [Fact]
    public void 新设置项参与相等性判定()
    {
        // record 的比较是手写的：漏加字段会导致「设置改了却被判定为没变」。
        Assert.NotEqual(AppSettings.Default, AppSettings.Default with { ClearClipboardOnDelete = false });
    }

    [Fact]
    public void 旧设置文件缺少新字段时取默认值()
    {
        // 老版本写下的 settings.json 里没有 clearClipboardOnDelete 字段：
        // 源生成反序列化只会给出 bool 零值 false，必须靠 schemaVersion 迁移补成「默认开启」，
        // 否则老用户升级后这个功能会静默失效（界面上还看不出原因）。
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(
            path,
            """
            { "schemaVersion": 1, "maxItems": 50, "maskSensitiveData": false }
            """);

        var settings = new SettingsStore(path).Load();

        Assert.True(settings.ClearClipboardOnDelete);
        Assert.Equal(50, settings.MaxItems);
        Assert.False(settings.MaskSensitiveData);
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void 新版本设置文件里的显式关闭会被尊重()
    {
        // 迁移不能倒过来把用户明确关掉的开关又打开：只有「旧版本、字段可能缺失」才补默认值。
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(
            path,
            $$"""
            { "schemaVersion": {{AppSettings.CurrentSchemaVersion}}, "clearClipboardOnDelete": false }
            """);

        Assert.False(new SettingsStore(path).Load().ClearClipboardOnDelete);
    }

    [Fact]
    public void 新设置项可持久化并读回()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsStore(path);

        Assert.True(store.TrySave(AppSettings.Default with { ClearClipboardOnDelete = false }, out var error), error);

        Assert.False(new SettingsStore(path).Load().ClearClipboardOnDelete);
    }
}
