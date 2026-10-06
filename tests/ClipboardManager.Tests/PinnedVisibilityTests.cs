using System.IO;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Settings;
using ClipboardManager.Core.Text;
using ClipboardManager.Core.Ui;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 收藏条目的可见性（用户 2026-10-06 两轮反馈后定稿）。
/// <para>
/// 定稿结论：收藏<b>不置顶</b> —— 置顶会把新复制的内容一路往下推，收藏一多就得翻半天
/// （用户 2026-10-06 拍板回退 B-16）。想看收藏时用面板底部的「只看收藏」筛选
/// （<see cref="PinnedFilterTests"/>）。
/// </para>
/// <para>
/// 收藏之所以还"看得见"，靠的是<b>显示上限与保留上限解耦</b>：面板固定显示
/// <see cref="PanelDisplay.MaxItems"/> 条，与设置里的「记录条数上限」无关，
/// 因此收藏不会再被 <c>LIMIT</c> 截断挤出列表（B-16 的真实根因）。
/// </para>
/// </summary>
public sealed class PinnedVisibilityTests : IDisposable
{
    private readonly string _directory;
    private readonly SqliteHistoryRepository _repository;

    public PinnedVisibilityTests()
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
    public void 收藏不置顶_列表是纯时间序()
    {
        // 收藏"最旧"的那条：它必须停在自己的时间位置（最后），不能被推到最前。
        var pinnedId = Insert("oldest-pinned", secondsAgo: 100);
        for (var index = 0; index < 3; index++)
        {
            _ = Insert($"normal-{index}", secondsAgo: 90 - index * 10);
        }

        Assert.True(_repository.SetPinned(pinnedId, true));

        var items = _repository.GetRecent(PanelDisplay.MaxItems);

        Assert.Equal(4, items.Count);
        Assert.False(items[0].IsPinned);
        Assert.Equal("normal-2", items[0].Preview);
        Assert.True(items[^1].IsPinned);
        Assert.Equal("oldest-pinned", items[^1].Preview);
    }

    [Fact]
    public void 显示上限固定500_收藏的旧记录不会被截断()
    {
        // B-16 的用户现象：保留上限设小时（如 50），最旧的收藏会被 `LIMIT 50` 截断挤出列表。
        // 解耦后显示固定取 500（与保留上限无关），只要库里还在就一定显示得出来。
        var pinnedId = Insert("oldest-pinned", secondsAgo: 10_000);
        for (var index = 0; index < 60; index++)
        {
            _ = Insert($"normal-{index}", secondsAgo: 5_000 - index);
        }

        Assert.True(_repository.SetPinned(pinnedId, true));

        // 面板实际使用的显示条数：保留上限 50 不再影响它。
        var items = _repository.GetRecent(PanelDisplay.Limit(retentionMaxItems: 50));

        Assert.Equal(61, items.Count);
        Assert.Contains(items, item => item.IsPinned && item.Preview == "oldest-pinned");
    }

    [Fact]
    public void 搜索同样按时间序_不把收藏提前()
    {
        var pinnedId = Insert("kw-pinned", secondsAgo: 100);
        _ = Insert("kw-normal", secondsAgo: 1);
        Assert.True(_repository.SetPinned(pinnedId, true));

        var hits = _repository.Search("kw", PanelDisplay.MaxItems);

        Assert.Equal(2, hits.Count);
        Assert.False(hits[0].IsPinned);
        Assert.Equal("kw-normal", hits[0].Preview);
        Assert.True(hits[1].IsPinned);
    }

    [Fact]
    public void 只看收藏时仍按最近时间排序()
    {
        var older = Insert("pinned-older", secondsAgo: 100);
        var newer = Insert("pinned-newer", secondsAgo: 1);
        Assert.True(_repository.SetPinned(older, true));
        Assert.True(_repository.SetPinned(newer, true));

        var items = _repository.GetRecent(PanelDisplay.MaxItems, pinnedOnly: true);

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

        // 上限 2：只约束非收藏，收藏永远保留（淘汰一直豁免收藏，与显示顺序无关）。
        var removed = _repository.EnforceMaxItems(2);

        Assert.Equal(4, removed.RemovedCount);
        Assert.Equal(3, _repository.CountAll());                       // 2 非收藏 + 1 收藏
        Assert.Contains(
            _repository.GetRecent(PanelDisplay.MaxItems),
            item => item.IsPinned && item.Preview == "keep-me");
    }

    [Fact]
    public void 显示上限与保留上限解耦_恒为硬上限()
    {
        // 守住这条：显示上限一旦又被写成 `Math.Min(保留上限, 硬上限)`，上限选 50 时
        // 库里的记录就会被截断挤出列表（B-16 的根因）。
        foreach (var retentionMaxItems in AppSettings.MaxItemsOptions)
        {
            Assert.Equal(PanelDisplay.MaxItems, PanelDisplay.Limit(retentionMaxItems));
        }

        Assert.Equal(500, PanelDisplay.MaxItems);
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
