namespace ClipboardManager.Core.Clipboard;

/// <summary>
/// 「当前系统剪贴板里装的到底是哪一条历史记录」的记账本（2026-09-19 新增：删除即吊销）。
/// <para>
/// 设计要点：本类只记一个事实 —— 「观察到序列号 <c>sequence</c> 时，剪贴板里是记录 <c>X</c>」，
/// 而<b>所有对外判定都必须带上实时序列号</b>。剪贴板一旦被任何程序改写，系统序列号必然变化，
/// 旧记账随即自动失效（不需要我们收到通知，也不需要清理）。
/// </para>
/// <para>
/// 因此本类对「漏报」是安全的：无论是被排除的程序、不支持的格式还是读取失败导致的漏记，
/// 结果都只是「判定为不是当前内容」，绝不会把别的内容误判成我们这条。
/// </para>
/// <para>线程安全：捕获路径在后台线程登记、判定发生在 UI 线程，故内部加锁。</para>
/// </summary>
public sealed class ClipboardPresenceTracker
{
    /// <summary>「未跟踪」的序列号哨兵值（真实序列号从 0 开始递增，不会是负数）。</summary>
    private const long UnknownSequence = -1;

    private readonly object _gate = new();
    private long? _itemId;
    private long _sequence = UnknownSequence;

    /// <summary>当前被跟踪的记录主键（null 表示剪贴板内容不属于任何一条记录）。</summary>
    public long? ItemId
    {
        get
        {
            lock (_gate)
            {
                return _itemId;
            }
        }
    }

    /// <summary>
    /// 登记「序列号 <paramref name="sequence"/> 对应的剪贴板内容 = 记录 <paramref name="itemId"/>」。
    /// </summary>
    /// <param name="sequence">读取该内容时（锁内）取得的剪贴板序列号。</param>
    /// <param name="itemId">该内容在历史里的记录主键。</param>
    public void NoteCaptured(long sequence, long itemId)
    {
        lock (_gate)
        {
            _itemId = itemId;
            _sequence = sequence;
        }
    }

    /// <summary>
    /// 指定记录此刻是否正是剪贴板内容 —— 面板「剪贴板中」徽标与删除吊销判定的共同依据。
    /// </summary>
    /// <param name="itemId">待判定的记录主键。</param>
    /// <param name="currentSequence">实时剪贴板序列号（<see cref="ClipboardAccess"/> 的
    /// <c>GetSequenceNumber</c>，无需打开剪贴板）。</param>
    public bool IsCurrent(long itemId, long currentSequence)
    {
        lock (_gate)
        {
            return _itemId == itemId && _sequence == currentSequence;
        }
    }

    /// <summary>
    /// 剪贴板此刻装的是否是「我们跟踪着的某条记录」（清空历史前判定用：
    /// 全部记录都将被删除，所以只要装的是一条被跟踪的记录，就该一并清掉）。
    /// </summary>
    /// <param name="currentSequence">实时剪贴板序列号。</param>
    public bool HoldsTrackedItem(long currentSequence)
    {
        lock (_gate)
        {
            return _itemId is not null && _sequence == currentSequence;
        }
    }

    /// <summary>清空记账（用于剪贴板被我们自己清空之后）。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _itemId = null;
            _sequence = UnknownSequence;
        }
    }
}
