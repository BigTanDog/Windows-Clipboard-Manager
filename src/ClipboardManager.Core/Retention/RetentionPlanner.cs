namespace ClipboardManager.Core.Retention;

/// <summary>淘汰决策所需的最小信息（从数据库读出，纯数据）。</summary>
/// <param name="Id">记录主键。</param>
/// <param name="IsPinned">是否收藏（收藏永不自动淘汰）。</param>
/// <param name="SizeBytes">入库时记录的体积（图片/HTML 为本体字节数）。</param>
/// <param name="UpdatedAtUnix">最近一次复制时间（Unix 秒）。</param>
public sealed record RetentionCandidate(long Id, bool IsPinned, long SizeBytes, long UpdatedAtUnix);

/// <summary>磁盘上限的规划结果。</summary>
/// <param name="EvictIds">建议淘汰的记录 id（顺序即建议顺序）。</param>
/// <param name="TotalBytes">规划前的总体积。</param>
/// <param name="PinnedBytes">其中收藏占用的体积。</param>
/// <param name="OverQuotaAfterEviction">清空全部非收藏后是否仍超限（此时只剩收藏，需求要求提示用户而不是自动删）。</param>
public sealed record DiskQuotaPlan(
    IReadOnlyList<long> EvictIds,
    long TotalBytes,
    long PinnedBytes,
    bool OverQuotaAfterEviction);

/// <summary>
/// 淘汰策略（需求 §3.3 + 产品计划 D-09）。
/// <para>
/// 这里是<b>纯决策</b>：只算出「该淘汰哪些 id」，真正的删除与文件清理由调用方执行。
/// 这样策略可以脱离数据库与磁盘做完整单测（AGENTS.md §2）。
/// </para>
/// </summary>
public static class RetentionPlanner
{
    /// <summary>
    /// 条数上限：只约束非收藏记录，保留最近的 <paramref name="maxItems"/> 条，
    /// 其余按「最旧优先」淘汰。
    /// </summary>
    /// <param name="items">全部记录。</param>
    /// <param name="maxItems">条数上限；-1 表示不限制。</param>
    public static IReadOnlyList<long> PlanByCount(IReadOnlyList<RetentionCandidate> items, int maxItems)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (maxItems < 0)
        {
            return [];
        }

        return
        [
            .. items
                .Where(static item => !item.IsPinned)
                .OrderByDescending(static item => item.UpdatedAtUnix)
                .ThenByDescending(static item => item.Id)
                .Skip(maxItems)
                .Select(static item => item.Id),
        ];
    }

    /// <summary>
    /// 磁盘占用上限：超限时优先淘汰<b>体积最大</b>的非收藏记录，同级按<b>最旧</b>优先；
    /// 收藏项永不自动淘汰，若只剩收藏仍超限则标记 <see cref="DiskQuotaPlan.OverQuotaAfterEviction"/>。
    /// </summary>
    /// <param name="items">全部记录。</param>
    /// <param name="quotaBytes">磁盘上限（字节）；-1 表示不限制。</param>
    public static DiskQuotaPlan PlanByDiskQuota(IReadOnlyList<RetentionCandidate> items, long quotaBytes)
    {
        ArgumentNullException.ThrowIfNull(items);

        var total = items.Sum(static item => Math.Max(item.SizeBytes, 0));
        var pinnedBytes = items.Where(static item => item.IsPinned).Sum(static item => Math.Max(item.SizeBytes, 0));

        if (quotaBytes < 0 || total <= quotaBytes)
        {
            return new DiskQuotaPlan([], total, pinnedBytes, false);
        }

        var evict = new List<long>();
        var remaining = total;

        foreach (var candidate in items
                     .Where(static item => !item.IsPinned)
                     .OrderByDescending(static item => item.SizeBytes)
                     .ThenBy(static item => item.UpdatedAtUnix)
                     .ThenBy(static item => item.Id))
        {
            if (remaining <= quotaBytes)
            {
                break;
            }

            evict.Add(candidate.Id);
            remaining -= Math.Max(candidate.SizeBytes, 0);
        }

        return new DiskQuotaPlan(evict, total, pinnedBytes, remaining > quotaBytes);
    }
}
