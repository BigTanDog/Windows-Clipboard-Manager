using System.IO;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Text;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 收藏项在列表里的优先级（用户 2026-10-06 反馈：达到条数上限后，收藏的条目也会从列表里消失，
/// 看起来像被删掉了）。
/// <para>
/// 实情：淘汰逻辑本来就不删收藏（<see cref="RetentionPlanner"/> 只淘汰非收藏），
/// 但<b>显示上限</b>等于条数上限，收藏若排在后面就会被截断挤出去 —— 所以查询要按「收藏优先」排序。
/// </para>
/// </summary>
public sealed class PinnedOrderingTests : IDisposable
{
    private readonly string _directory;
    private readonly SqliteHistoryRepository _repository;

    public PinnedOrderingTests()
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
    public void 收藏条目排在最前_不会被显示上限挤出()
    {
        // 收藏的是「最旧」的那条：不置顶的话它正是第一个被截断掉的
        var pinnedId = Insert("oldest-pinned");
        for (var index = 0; index < 5; index++)
        {
            _ = Insert($"normal-{index}");
        }

        Assert.True(_repository.SetPinned(pinnedId, true));

        var limited = _repository.GetRecent(2);

        Assert.Equal(2, limited.Count);
        Assert.True(limited[0].IsPinned);
        Assert.Equal("oldest-pinned", limited[0].Preview);
    }

    [Fact]
    public void 搜索同样把收藏置顶()
    {
        var pinnedId = Insert("kw-pinned");
        _ = Insert("kw-normal");
        Assert.True(_repository.SetPinned(pinnedId, true));

        var hits = _repository.Search("kw", 10);

        Assert.Equal(2, hits.Count);
        Assert.True(hits[0].IsPinned);
        Assert.Equal("kw-pinned", hits[0].Preview);
    }

    [Fact]
    public void 只看收藏时仍按最近时间排序()
    {
        var older = Insert("pinned-older", secondsAgo: 100);
        var newer = Insert("pinned-newer", secondsAgo: 1);
        Assert.True(_repository.SetPinned(older, true));
        Assert.True(_repository.SetPinned(newer, true));

        var items = _repository.GetRecent(10, pinnedOnly: true);

        Assert.Equal(2, items.Count);
        Assert.Equal("pinned-newer", items[0].Preview);
        Assert.Equal("pinned-older", items[1].Preview);
    }

    [Fact]
    public void 收藏不占条数上限的额度()
    {
        for (var index = 0; index < 6; index++)
        {
            _ = Insert($"n-{index}", secondsAgo: 100 - index);
        }

        var pinnedId = Insert("keep-me", secondsAgo: 50);
        Assert.True(_repository.SetPinned(pinnedId, true));

        // 上限 2：只约束非收藏，收藏永远保留
        var removed = _repository.EnforceMaxItems(2);

        Assert.Equal(4, removed.RemovedCount);
        Assert.Equal(3, _repository.CountAll());                       // 2 非收藏 + 1 收藏
        Assert.Contains(_repository.GetRecent(10), item => item.IsPinned && item.Preview == "keep-me");
    }

    private long Insert(string text, int secondsAgo = 0) =>
        _repository.Upsert(
            new ClipCandidate
            {
                Type = ClipContentType.Text,
                Text = text,
                CapturedAt = DateTimeOffset.Now.AddSeconds(-secondsAgo),
            },
            ContentHasher.ForText(text),
            text).Id;
}
