using System.IO;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Retention;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 淘汰链路的集成测试：真实 SQLite + 真实 <see cref="BlobStore"/> 文件，
/// 覆盖「规划 → 删记录 → 删本体文件 → 收藏保护」的完整路径（AGENTS.md §2）。
/// <para>
/// 说明：<c>size_bytes</c> 直接写入大数值，避免测试真的产生几百 MB 文件
/// —— 磁盘上限的计量口径本就是数据库里的 <c>size_bytes</c> 合计（见技术设计 §4.5）。
/// </para>
/// </summary>
public sealed class RetentionIntegrationTests : IDisposable
{
    private readonly string _directory;
    private readonly SqliteHistoryRepository _repository;
    private readonly BlobStore _blobs;

    public RetentionIntegrationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "clipboard-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _repository = new SqliteHistoryRepository(Path.Combine(_directory, "history.db"));
        _repository.Initialize();
        _blobs = new BlobStore(_directory);
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
            // 清理失败不影响结论
        }
    }

    [Fact]
    public void 磁盘上限触发时同时删除记录与本体文件()
    {
        // 三条各记 100MB（本体文件很小，仅用于验证文件也被清掉）
        var (id1, path1) = InsertWithBlob(1, 100L * 1024 * 1024);
        var (id2, path2) = InsertWithBlob(2, 100L * 1024 * 1024);
        var (id3, path3) = InsertWithBlob(3, 100L * 1024 * 1024);

        // 上限 200MB → 必须淘汰一条；三条同体积按最旧优先
        var plan = RetentionPlanner.PlanByDiskQuota(_repository.GetRetentionCandidates(), 200L * 1024 * 1024);

        Assert.Single(plan.EvictIds);
        Assert.Equal(id1, plan.EvictIds[0]);
        Assert.False(plan.OverQuotaAfterEviction);

        foreach (var blobPath in _repository.DeleteMany(plan.EvictIds))
        {
            _blobs.TryDelete(blobPath);
        }

        Assert.Equal(2, _repository.CountAll());
        Assert.False(File.Exists(Path.Combine(_directory, path1)));
        Assert.True(File.Exists(Path.Combine(_directory, path2)));
        Assert.True(File.Exists(Path.Combine(_directory, path3)));
        Assert.DoesNotContain(_repository.GetReferencedBlobPaths(), path => path == path1);
        Assert.NotNull(_repository.GetById(id2));
        Assert.NotNull(_repository.GetById(id3));
        Assert.Null(_repository.GetById(id1));
    }

    [Fact]
    public void 收藏项在磁盘上限下不被删除()
    {
        var (pinnedId, pinnedPath) = InsertWithBlob(7, 500L * 1024 * 1024, pinned: true);
        var (normalId, normalPath) = InsertWithBlob(8, 300L * 1024 * 1024);

        var plan = RetentionPlanner.PlanByDiskQuota(_repository.GetRetentionCandidates(), 200L * 1024 * 1024);

        // 非收藏的被淘汰；收藏的仍在（即便它自己就超限），并给出提示标记
        Assert.Equal([normalId], plan.EvictIds);
        Assert.True(plan.OverQuotaAfterEviction);

        foreach (var blobPath in _repository.DeleteMany(plan.EvictIds))
        {
            _blobs.TryDelete(blobPath);
        }

        Assert.NotNull(_repository.GetById(pinnedId));
        Assert.Null(_repository.GetById(normalId));
        Assert.True(File.Exists(Path.Combine(_directory, pinnedPath)));
        Assert.False(File.Exists(Path.Combine(_directory, normalPath)));
    }

    [Fact]
    public void 条数上限与收藏保护联动()
    {
        for (var index = 0; index < 5; index++)
        {
            _ = InsertWithBlob(index, 1024);
        }

        // 把最旧的一条设为收藏
        var all = _repository.GetRetentionCandidates().OrderBy(static item => item.UpdatedAtUnix).ToArray();
        var oldest = all[0];
        Assert.True(_repository.SetPinned(oldest.Id, true));

        var doomed = RetentionPlanner.PlanByCount(_repository.GetRetentionCandidates(), 2);
        _ = _repository.DeleteMany(doomed);

        // 收藏的那条必须还在
        Assert.NotNull(_repository.GetById(oldest.Id));
        Assert.Equal(3, _repository.CountAll()); // 收藏 1 条 + 保留的 2 条
    }

    [Fact]
    public void 取消收藏后重新参与淘汰()
    {
        var (id, _) = InsertWithBlob(11, 1024);
        Assert.True(_repository.SetPinned(id, true));

        var pinned = _repository.GetRetentionCandidates().Single();
        Assert.True(pinned.IsPinned);

        Assert.True(_repository.SetPinned(id, false));
        Assert.False(_repository.GetRetentionCandidates().Single().IsPinned);
    }

    /// <summary>本体存储要求内容哈希是小写十六进制（非法哈希会被拒绝），这里按序号造一个合法的。</summary>
    private static string HashOf(int index) => index.ToString("x8", System.Globalization.CultureInfo.InvariantCulture) + new string('f', 56);

    private (long Id, string BlobPath) InsertWithBlob(int index, long sizeBytes, bool pinned = false)
    {
        var hash = HashOf(index);
        var blobPath = _blobs.Save([1, 2, 3, 4], hash, "png");
        var outcome = _repository.Upsert(
            new ClipCandidate { Type = ClipContentType.Image, CapturedAt = DateTimeOffset.Now },
            hash,
            "100×100 · 1 KB",
            blobPath,
            sizeBytes);

        if (pinned)
        {
            _ = _repository.SetPinned(outcome.Id, true);
        }

        return (outcome.Id, blobPath);
    }
}
