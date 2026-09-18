using System.IO;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Text;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>本体存储测试（技术设计 §7.2）：原子写、幂等、路径越权防护、孤儿回收。</summary>
public sealed class BlobStoreTests : IDisposable
{
    private const string HashA = "aa11bb22cc33dd44ee55ff6600112233445566778899aabbccddeeff00112233";
    private const string HashB = "bb11bb22cc33dd44ee55ff6600112233445566778899aabbccddeeff00112233";

    private readonly string _directory;
    private readonly BlobStore _store;

    public BlobStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "clipboard-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new BlobStore(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响结论
        }
    }

    [Fact]
    public void 保存后可读回_且路径按前两位分桶()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var relative = _store.Save(data, HashA, "png");

        Assert.Equal(Path.Combine("blobs", "aa", $"{HashA}.png"), relative);
        Assert.Equal(data, _store.TryRead(relative));
        Assert.True(File.Exists(Path.Combine(_directory, relative)));
    }

    [Fact]
    public void 相同哈希重复保存_复用同一文件()
    {
        var first = _store.Save([1, 2, 3], HashA, "png");
        var second = _store.Save([9, 9, 9], HashA, "png");

        Assert.Equal(first, second);
        // 内容寻址：第二次不覆盖（同哈希意味着同内容）
        Assert.Equal(new byte[] { 1, 2, 3 }, _store.TryRead(first));
    }

    [Fact]
    public void 缩略图路径_同目录加thumb后缀()
    {
        var blob = _store.Save([1], HashA, "png");
        var thumb = BlobStore.ThumbnailPathFor(blob);

        Assert.Equal(Path.Combine("blobs", "aa", $"{HashA}.thumb.png"), thumb);
        Assert.Null(BlobStore.ThumbnailPathFor(null));
    }

    [Fact]
    public void 删除本体_幂等()
    {
        var blob = _store.Save([1, 2], HashA, "html");

        Assert.True(_store.TryDelete(blob));
        Assert.False(_store.TryDelete(blob));
        Assert.Null(_store.TryRead(blob));
    }

    [Fact]
    public void 越权路径_被拒绝且不抛给调用方()
    {
        // 目录穿越：既不能读到 data 目录之外的文件，也不能让它抛出
        Assert.Null(_store.TryRead(Path.Combine("..", "..", "secret.txt")));
        Assert.False(_store.TryDelete(Path.Combine("..", "..", "secret.txt")));
    }

    [Fact]
    public void 非白名单扩展名_抛异常() =>
        Assert.Throws<ArgumentException>(() => _store.Save([1], HashA, "exe"));

    [Fact]
    public void 非法哈希_抛异常()
    {
        Assert.Throws<ArgumentException>(() => _store.Save([1], "not-a-hash", "png"));
        Assert.Throws<ArgumentException>(() => _store.Save([1], "aa", "png"));
    }

    [Fact]
    public void 孤儿回收_删除未引用文件与临时文件()
    {
        var kept = _store.Save([1, 2, 3], HashA, "png");
        var orphan = _store.Save([4, 5, 6], HashB, "png");
        _ = _store.Save([7], HashB, "png", thumbnail: true);

        // 顺带放一个残留的 .tmp
        var temp = Path.Combine(_directory, "blobs", "bb", "leftover.tmp");
        File.WriteAllBytes(temp, [0]);

        var removed = _store.CollectOrphans([kept]);

        Assert.Equal(3, removed);
        Assert.NotNull(_store.TryRead(kept));
        Assert.False(File.Exists(Path.Combine(_directory, orphan)));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void 引用路径存在时_对应缩略图不被误删()
    {
        var blob = _store.Save([1, 2, 3], HashA, "png");
        var thumb = _store.Save([9], HashA, "png", thumbnail: true);

        var removed = _store.CollectOrphans([blob]);

        Assert.Equal(0, removed);
        Assert.NotNull(_store.TryRead(thumb));
    }
}

/// <summary>搜索片段提取测试。</summary>
public class SearchSnippetTests
{
    [Fact]
    public void 命中在中间_带前后省略号()
    {
        var text = new string('a', 200) + "NEEDLE" + new string('b', 200);

        var snippet = SearchSnippet.Extract(text, "needle", 40);

        Assert.Contains("NEEDLE", snippet, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("…", snippet, StringComparison.Ordinal);
        Assert.EndsWith("…", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void 无命中_退化为开头截断()
    {
        var snippet = SearchSnippet.Extract(new string('x', 300), "zzz", 50);

        Assert.Equal(51, snippet.Length); // 50 字符 + 省略号
        Assert.EndsWith("…", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void 短文本原样返回() =>
        Assert.Equal("短文本", SearchSnippet.Extract("短文本", "文本", 50));

    [Fact]
    public void 空输入安全()
    {
        Assert.Equal(string.Empty, SearchSnippet.Extract(null, "a"));
        Assert.Equal(string.Empty, SearchSnippet.Extract("abc", "a", 0));
    }
}

/// <summary>仓储的阶段二能力：搜索（含 LIKE 转义）、本体字段、淘汰返回本体路径。</summary>
public sealed class RepositorySearchTests : IDisposable
{
    private readonly string _directory;
    private readonly SqliteHistoryRepository _repository;

    public RepositorySearchTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "clipboard-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _repository = new SqliteHistoryRepository(Path.Combine(_directory, "history.db"));
        _repository.Initialize();
    }

    public void Dispose()
    {
        _repository.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 忽略
        }
    }

    [Fact]
    public void 搜索文本内容_大小写不敏感()
    {
        Insert("Hello World", "hello world");
        Insert("另一条记录", "另一条记录");

        var hits = _repository.Search("HELLO", 10);

        Assert.Single(hits);
        Assert.Equal("hello world", hits[0].Preview);
    }

    [Fact]
    public void 搜索文件路径()
    {
        _repository.Upsert(
            new ClipCandidate
            {
                Type = ClipContentType.FileList,
                FilePaths = ["C:\\Temp\\报告.pptx"],
                CapturedAt = DateTimeOffset.Now,
            },
            ContentHasher.ForFileList(["C:\\Temp\\报告.pptx"]),
            "1 个文件：报告.pptx");

        Assert.Single(_repository.Search("报告", 10));
        Assert.Single(_repository.Search("pptx", 10));
    }

    [Fact]
    public void LIKE通配符被转义_按字面匹配()
    {
        Insert("折扣 50%off 促销", "折扣 50%off 促销");
        Insert("完全无关", "完全无关");

        // 若未转义，"%" 会匹配任意内容；这里必须只命中包含字面 "50%" 的那条
        var hits = _repository.Search("50%", 10);

        Assert.Single(hits);
        Assert.Contains("50%off", hits[0].Preview, StringComparison.Ordinal);
    }

    [Fact]
    public void 下划线也被转义()
    {
        Insert("a_b", "a_b");
        Insert("aXb", "aXb");

        var hits = _repository.Search("a_b", 10);

        Assert.Single(hits);
        Assert.Equal("a_b", hits[0].Preview);
    }

    [Fact]
    public void 空查询退化为最近记录()
    {
        Insert("first", "first");
        Insert("second", "second");

        Assert.Equal(2, _repository.Search("   ", 10).Count);
    }

    [Fact]
    public void 本体路径与体积入库并可读回()
    {
        var outcome = _repository.Upsert(
            new ClipCandidate { Type = ClipContentType.Image, CapturedAt = DateTimeOffset.Now },
            "hash-image",
            "100×50 · 2 KB",
            "blobs/ha/hash-image.png",
            2048);

        var item = _repository.GetById(outcome.Id);

        Assert.NotNull(item);
        Assert.Equal("blobs/ha/hash-image.png", item.BlobPath);
        Assert.Equal(2048, item.SizeBytes);
        Assert.Equal(ClipContentType.Image, item.Type);
        Assert.Equal(["blobs/ha/hash-image.png"], _repository.GetReferencedBlobPaths());
    }

    [Fact]
    public void 去重命中时不覆盖本体路径()
    {
        _repository.Upsert(
            new ClipCandidate { Type = ClipContentType.Text, Text = "same", CapturedAt = DateTimeOffset.Now.AddSeconds(-5) },
            "hash-same",
            "same",
            "blobs/x/hash-same.png",
            10);

        var second = _repository.Upsert(
            new ClipCandidate { Type = ClipContentType.Text, Text = "same", CapturedAt = DateTimeOffset.Now },
            "hash-same",
            "same",
            null,
            99);

        Assert.False(second.IsNew);
        var item = _repository.GetById(second.Id);
        Assert.NotNull(item);
        Assert.Equal("blobs/x/hash-same.png", item.BlobPath);
    }

    [Fact]
    public void 淘汰返回被删记录的本体路径()
    {
        for (var index = 0; index < 3; index++)
        {
            _repository.Upsert(
                new ClipCandidate { Type = ClipContentType.Image, CapturedAt = DateTimeOffset.Now.AddSeconds(-100 + index) },
                $"hash-{index}",
                $"p{index}",
                $"blobs/aa/hash-{index}.png",
                100);
        }

        var evicted = _repository.EnforceMaxItems(1);

        Assert.Equal(2, evicted.RemovedCount);
        Assert.Equal(2, evicted.BlobPaths.Count);
        Assert.Contains("blobs/aa/hash-0.png", evicted.BlobPaths);
    }

    private void Insert(string text, string preview) =>
        _repository.Upsert(
            new ClipCandidate { Type = ClipContentType.Text, Text = text, CapturedAt = DateTimeOffset.Now },
            ContentHasher.ForText(text),
            preview);
}
