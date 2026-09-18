using ClipboardManager.Core.Models;
using ClipboardManager.Core.Text;
using ClipboardManager.Storage;
using Microsoft.Data.Sqlite;

namespace ClipboardManager.Tests;

/// <summary>
/// 存储层集成测试：使用临时目录下的真实 SQLite 文件（不使用内存库，确保 PRAGMA 与文件行为一致）。
/// </summary>
public sealed class SqliteHistoryRepositoryTests : IDisposable
{
    private readonly string _directory;
    private readonly SqliteHistoryRepository _repository;

    public SqliteHistoryRepositoryTests()
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
    public void 初始化后可读写且表结构正确()
    {
        Assert.Equal(0, _repository.CountAll());

        var outcome = _repository.Upsert(Candidate("hello"), ContentHasher.ForText("hello"), "hello");
        Assert.True(outcome.IsNew);
        Assert.Equal(1, _repository.CountAll());
    }

    [Fact]
    public void 相同内容去重只刷新时间戳()
    {
        var first = _repository.Upsert(Candidate("dup", secondsAgo: 10), ContentHasher.ForText("dup"), "dup");
        var second = _repository.Upsert(Candidate("dup", secondsAgo: 0), ContentHasher.ForText("dup"), "dup");

        Assert.True(first.IsNew);
        Assert.False(second.IsNew);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, _repository.CountAll());

        var item = _repository.GetRecent(10)[0];
        Assert.Equal(second.UpdatedAt.ToUnixTimeSeconds(), item.UpdatedAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void 按更新时间倒序返回()
    {
        _repository.Upsert(Candidate("old", secondsAgo: 100), ContentHasher.ForText("old"), "old");
        _repository.Upsert(Candidate("new", secondsAgo: 1), ContentHasher.ForText("new"), "new");

        var items = _repository.GetRecent(10);
        Assert.Equal("new", items[0].Preview);
        Assert.Equal("old", items[1].Preview);
    }

    [Fact]
    public void 条数上限淘汰最旧的非收藏项()
    {
        for (var index = 0; index < 5; index++)
        {
            _repository.Upsert(Candidate($"item-{index}", secondsAgo: 100 - index), ContentHasher.ForText($"item-{index}"), $"item-{index}");
        }

        var removed = _repository.EnforceMaxItems(2);

        Assert.Equal(3, removed);
        Assert.Equal(2, _repository.CountAll());
        var remaining = _repository.GetRecent(10);
        Assert.Equal("item-4", remaining[0].Preview);
        Assert.Equal("item-3", remaining[1].Preview);
    }

    [Fact]
    public void 条数上限为不限制时不淘汰()
    {
        _repository.Upsert(Candidate("a"), ContentHasher.ForText("a"), "a");
        Assert.Equal(0, _repository.EnforceMaxItems(-1));
        Assert.Equal(1, _repository.CountAll());
    }

    [Fact]
    public void 收藏项不参与淘汰()
    {
        for (var index = 0; index < 3; index++)
        {
            _repository.Upsert(Candidate($"p-{index}", secondsAgo: 100 - index), ContentHasher.ForText($"p-{index}"), $"p-{index}");
        }

        // 阶段一没有收藏 UI，这里直接用 SQL 把「最旧」一条标记为收藏，验证淘汰保护。
        PinOldest();

        // 上限 1 只约束「非收藏」：p-2（最新非收藏）保留，p-0（收藏）保留，仅淘汰 p-1
        var removed = _repository.EnforceMaxItems(1);
        var remaining = _repository.GetRecent(10);

        Assert.Equal(1, removed);
        Assert.Equal(2, _repository.CountAll());
        Assert.Contains(remaining, item => item.IsPinned && item.Preview == "p-0");
        Assert.Contains(remaining, item => !item.IsPinned && item.Preview == "p-2");
    }

    [Fact]
    public void 删除单条与清空()
    {
        var outcome = _repository.Upsert(Candidate("x"), ContentHasher.ForText("x"), "x");
        Assert.True(_repository.Delete(outcome.Id));
        Assert.Equal(0, _repository.CountAll());
        Assert.False(_repository.Delete(outcome.Id));

        _repository.Upsert(Candidate("y"), ContentHasher.ForText("y"), "y");
        _repository.Upsert(Candidate("z"), ContentHasher.ForText("z"), "z");
        Assert.Equal(2, _repository.DeleteAll());
        Assert.Equal(0, _repository.CountAll());
    }

    [Fact]
    public void 体积合计用于磁盘上限判定()
    {
        _repository.Upsert(Candidate("12345"), ContentHasher.ForText("12345"), "12345");
        _repository.Upsert(Candidate("abc"), ContentHasher.ForText("abc"), "abc");

        // SizeBytes = 文本长度 * 2
        Assert.Equal(16, _repository.SumSizeBytes());
    }

    [Fact]
    public void 数据库文件确实落在指定目录()
    {
        _repository.Upsert(Candidate("file"), ContentHasher.ForText("file"), "file");
        Assert.True(File.Exists(Path.Combine(_directory, "history.db")));
    }

    private void PinOldest()
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "history.db")}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE clip_items SET is_pinned = 1 WHERE preview = 'p-0';";
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private static ClipCandidate Candidate(string text, int secondsAgo = 0) => new()
    {
        Type = ClipContentType.Text,
        Text = text,
        CapturedAt = DateTimeOffset.Now.AddSeconds(-secondsAgo),
    };
}
