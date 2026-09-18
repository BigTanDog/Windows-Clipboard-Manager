namespace ClipboardManager.Core.Clipboard;

/// <summary>
/// 剪贴板自循环过滤器（需求 §5.1，最高危的坑）。
/// <para>
/// 设计要点：<b>不依赖「写入期间置布尔标志」的时序假设</b> —— 官方文档并未写明
/// <c>WM_CLIPBOARDUPDATE</c> 是同步发送还是异步投递，如果按「写完立刻复位标志」实现，
/// 一旦消息晚到就会把自己的粘贴操作再记一条，形成循环。
/// </para>
/// <para>
/// 这里用两个互补判据：① <c>GetClipboardSequenceNumber</c> 精确命中（写入后系统返回的新序列号，
/// 收到同一个序列号一定是自己写的那次）；② 内容哈希 + 时间窗兜底（应对序列号在其它程序
/// 连锁写入后又被覆盖的情况）。
/// </para>
/// </summary>
public sealed class SelfWriteFilter
{
    /// <summary>哈希兜底判定的时间窗。</summary>
    public static readonly TimeSpan HashWindow = TimeSpan.FromSeconds(2);

    private long? _ownSequence;
    private string? _ownHash;
    private DateTimeOffset _ownAt = DateTimeOffset.MinValue;

    /// <summary>
    /// 记录一次「自己写入剪贴板」的事实。
    /// </summary>
    /// <param name="sequenceNumber">写入后立即读取的剪贴板序列号。</param>
    /// <param name="contentHash">写入内容的哈希（可为 null，表示不提供兜底判据）。</param>
    /// <param name="at">写入时刻。</param>
    public void NoteOwnWrite(long sequenceNumber, string? contentHash, DateTimeOffset at)
    {
        _ownSequence = sequenceNumber;
        _ownHash = contentHash;
        _ownAt = at;
    }

    /// <summary>
    /// 判断一次剪贴板更新是否应当忽略（即由本进程自身写入引起）。
    /// </summary>
    /// <param name="currentSequence">当前剪贴板序列号。</param>
    /// <param name="contentHash">当前内容哈希（可为 null）。</param>
    /// <param name="now">当前时刻。</param>
    public bool ShouldIgnore(long currentSequence, string? contentHash, DateTimeOffset now)
    {
        if (_ownSequence.HasValue && currentSequence == _ownSequence.Value)
        {
            return true;
        }

        if (_ownHash is null || contentHash is null)
        {
            return false;
        }

        var elapsed = now - _ownAt;
        if (elapsed < TimeSpan.Zero || elapsed > HashWindow)
        {
            return false;
        }

        return string.Equals(_ownHash, contentHash, StringComparison.Ordinal);
    }

    /// <summary>清空记录（例如注销监听时）。</summary>
    public void Reset()
    {
        _ownSequence = null;
        _ownHash = null;
        _ownAt = DateTimeOffset.MinValue;
    }
}
