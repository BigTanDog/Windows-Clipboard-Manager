using System.IO;
using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Text;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 「清空历史」默认保留收藏」的测试（用户 2026-09-30 反馈）。
/// <para>
/// 覆盖三件事：① 删除范围（<see cref="PurgeScope"/>）只含即将被删除的记录；
/// ② 存储层按收藏过滤的删除与计数；③ 保留收藏时，收藏记录的本体文件不会被孤儿清理误删。
/// </para>
/// </summary>
public sealed class ClearHistoryTests : IDisposable
{
    private readonly string _directory;
    private readonly SqliteHistoryRepository _repository;

    public ClearHistoryTests()
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
            // 测试清理失败不影响结论。
        }
    }

    [Fact]
    public void 默认清空只删非收藏_收藏条目保留()
    {
        var doomed = Insert("doomed");
        var kept = Insert("kept");
        Assert.True(_repository.SetPinned(kept, true));

        var targets = _repository.LoadClearTargets(includePinned: false);

        Assert.Equal(1, targets.Count);
        Assert.False(targets.IsEmpty);
        Assert.True(targets.ContainsId(doomed));
        Assert.False(targets.ContainsId(kept));
        Assert.True(targets.ContainsHash(ContentHasher.ForText("doomed")));
        Assert.False(targets.ContainsHash(ContentHasher.ForText("kept")));

        Assert.Equal(1, _repository.DeleteAll(includePinned: false));
        Assert.Equal(1, _repository.CountAll());
        Assert.Equal("kept", _repository.GetRecent(10)[0].Preview);
    }

    [Fact]
    public void 全部是收藏时删除范围为空且一条都不删()
    {
        var kept = Insert("only-pinned");
        Assert.True(_repository.SetPinned(kept, true));

        var targets = _repository.LoadClearTargets(includePinned: false);

        Assert.True(targets.IsEmpty);
        Assert.Equal(0, targets.Count);
        Assert.Equal(0, _repository.CountAll(includePinned: false));
        Assert.Equal(1, _repository.CountAll(includePinned: true));

        Assert.Equal(0, _repository.DeleteAll(includePinned: false));
        Assert.Equal(1, _repository.CountAll());
    }

    [Fact]
    public void 勾选删除收藏时删除范围等于全部记录()
    {
        _ = Insert("a");
        var pinned = Insert("b");
        Assert.True(_repository.SetPinned(pinned, true));

        var targets = _repository.LoadClearTargets(includePinned: true);

        Assert.Equal(_repository.CountAll(), targets.Count);
        Assert.True(targets.ContainsId(pinned));
        Assert.Equal(_repository.CountAll(includePinned: false), _repository.CountAll() - 1);

        Assert.Equal(2, _repository.DeleteAll(includePinned: true));
        Assert.Equal(0, _repository.CountAll());
    }

    [Fact]
    public void 保留收藏时收藏的本体文件不会被孤儿清理误删()
    {
        var blobs = new BlobStore(_directory);
        var keptHash = new string('c', 64);
        var doomedHash = new string('d', 64);
        var keptBlob = blobs.Save([4, 5, 6], keptHash, "png");
        var keptThumb = blobs.Save([7], keptHash, "png", thumbnail: true);
        var doomedBlob = blobs.Save([1, 2, 3], doomedHash, "png");

        var keptId = _repository.Upsert(Candidate("kept"), keptHash, "kept", keptBlob).Id;
        _ = _repository.Upsert(Candidate("doomed"), doomedHash, "doomed", doomedBlob);
        Assert.True(_repository.SetPinned(keptId, true));

        Assert.Equal(1, _repository.DeleteAll(includePinned: false));

        // 与 AppHost 清空历史后同一套口径：引用集合 = 删除后仍存在的记录。
        var removed = blobs.CollectOrphans(_repository.GetReferencedBlobPaths());

        Assert.Equal(1, removed);
        Assert.NotNull(blobs.TryRead(keptBlob));
        Assert.NotNull(blobs.TryRead(keptThumb));
        Assert.Null(blobs.TryRead(doomedBlob));
    }

    private long Insert(string text) =>
        _repository.Upsert(Candidate(text), ContentHasher.ForText(text), text).Id;

    private static ClipCandidate Candidate(string text) => new()
    {
        Type = ClipContentType.Text,
        Text = text,
        CapturedAt = DateTimeOffset.Now,
    };
}

/// <summary>「清空历史」删除范围（纯逻辑）的判定测试。</summary>
public class PurgeScopeTests
{
    [Fact]
    public void 按主键与哈希判定归属()
    {
        var scope = new PurgeScope([1, 2], ["h1", "h2"]);

        Assert.Equal(2, scope.Count);
        Assert.False(scope.IsEmpty);
        Assert.True(scope.ContainsId(1));
        Assert.True(scope.ContainsId(2));
        Assert.False(scope.ContainsId(3));
        Assert.True(scope.ContainsHash("h1"));
        Assert.False(scope.ContainsHash("h3"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 哈希为空时一律不属于范围(string? hash)
    {
        var scope = new PurgeScope([1], ["h1"]);
        Assert.False(scope.ContainsHash(hash));
    }

    [Fact]
    public void 空范围不包含任何记录()
    {
        var scope = new PurgeScope([], []);

        Assert.True(scope.IsEmpty);
        Assert.Equal(0, scope.Count);
        Assert.False(scope.ContainsId(0));
        Assert.False(scope.ContainsHash("h1"));
    }
}
