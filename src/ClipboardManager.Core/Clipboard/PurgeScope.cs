namespace ClipboardManager.Core.Clipboard;

/// <summary>
/// 「清空历史」的删除范围：哪些记录即将被删除（主键 + 内容哈希）。
/// <para>
/// 存在的理由是一条安全边界：清空历史时，<b>只有即将被删除的那些记录</b>才允许连带清空系统剪贴板。
/// 当用户选择「保留收藏」时，剪贴板里装的可能正是那条会被保留下来的收藏记录 ——
/// 它并没有消失，此时动剪贴板就是错的（用户会莫名失去刚复制的内容）。
/// </para>
/// <para>纯数据 + 纯判定，不含 Win32/WPF 依赖，可在无桌面环境下单测。</para>
/// </summary>
public sealed class PurgeScope
{
    private readonly HashSet<long> _ids;
    private readonly HashSet<string> _hashes;

    /// <summary>创建删除范围。</summary>
    /// <param name="ids">将被删除的记录主键。</param>
    /// <param name="hashes">将被删除记录的内容哈希（与入库去重键同一口径）。</param>
    public PurgeScope(IEnumerable<long> ids, IEnumerable<string> hashes)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(hashes);

        _ids = [.. ids];
        _hashes = [.. hashes];
    }

    /// <summary>将被删除的记录条数。</summary>
    public int Count => _ids.Count;

    /// <summary>是否没有任何记录会被删除（此时不该弹清空确认框）。</summary>
    public bool IsEmpty => _ids.Count == 0;

    /// <summary>该记录主键是否落在删除范围内。</summary>
    /// <param name="id">记录主键。</param>
    public bool ContainsId(long id) => _ids.Contains(id);

    /// <summary>该内容哈希是否落在删除范围内（按内容指纹兜底判定身份时使用）。</summary>
    /// <param name="hash">内容哈希；null / 空串表示无法判定，一律视为不在范围内。</param>
    public bool ContainsHash(string? hash) => !string.IsNullOrEmpty(hash) && _hashes.Contains(hash);
}
