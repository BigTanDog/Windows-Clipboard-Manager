using System.IO;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Text;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 「只看收藏」筛选在仓储层的过滤（面板底部星形按钮，用户 2026-10-05 要求）。
/// <para>筛选与搜索可叠加，所以 <c>pinnedOnly</c> 两条路径都要验。</para>
/// </summary>
public sealed class PinnedFilterTests : IDisposable
{
    private readonly string _directory;
    private readonly SqliteHistoryRepository _repository;

    public PinnedFilterTests()
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
    public void 只看收藏时不返回非收藏记录()
    {
        var pinnedId = Insert("pinned-one");
        _ = Insert("normal-one");
        Assert.True(_repository.SetPinned(pinnedId, true));

        var all = _repository.GetRecent(10);
        var pinnedOnly = _repository.GetRecent(10, pinnedOnly: true);

        Assert.Equal(2, all.Count);
        Assert.Single(pinnedOnly);
        Assert.Equal("pinned-one", pinnedOnly[0].Preview);
        Assert.True(pinnedOnly[0].IsPinned);
    }

    [Fact]
    public void 没有收藏时筛选返回空()
    {
        _ = Insert("only-normal");

        Assert.Empty(_repository.GetRecent(10, pinnedOnly: true));
    }

    [Fact]
    public void 搜索叠加只看收藏时只出收藏里的命中()
    {
        var pinnedId = Insert("keyword-pinned");
        _ = Insert("keyword-normal");
        Assert.True(_repository.SetPinned(pinnedId, true));

        var hitBoth = _repository.Search("keyword", 10);
        var hitPinned = _repository.Search("keyword", 10, pinnedOnly: true);

        Assert.Equal(2, hitBoth.Count);
        Assert.Single(hitPinned);
        Assert.Equal("keyword-pinned", hitPinned[0].Preview);
    }

    [Fact]
    public void 只命中非收藏时筛选不误放行()
    {
        // 这条守的是 SQL 的括号优先级：`A OR B OR C AND is_pinned = 1` 会把非收藏也搜出来
        _ = Insert("only-normal-hit");

        Assert.Empty(_repository.Search("only-normal-hit", 10, pinnedOnly: true));
    }

    [Fact]
    public void 空查询叠加只看收藏等价于只看收藏的最近记录()
    {
        var pinnedId = Insert("empty-query-pinned");
        _ = Insert("empty-query-normal");
        Assert.True(_repository.SetPinned(pinnedId, true));

        var result = _repository.Search("   ", 10, pinnedOnly: true);

        Assert.Single(result);
        Assert.Equal("empty-query-pinned", result[0].Preview);
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
