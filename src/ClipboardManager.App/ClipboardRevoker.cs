using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Models;
using ClipboardManager.Interop;
using ClipboardManager.Storage;

namespace ClipboardManager.App;

/// <summary>吊销（清空系统剪贴板）的结局。</summary>
internal enum RevokeStatus
{
    /// <summary>已清空系统剪贴板：该内容再也粘不出来。</summary>
    Revoked,

    /// <summary>剪贴板里不是这条记录 —— 只删记录，剪贴板保持不动。</summary>
    NotCurrent,

    /// <summary>未能判定或清空（多为剪贴板被其它程序占用）：剪贴板保持不动，并给出原因。</summary>
    Failed,
}

/// <summary>吊销结果。</summary>
/// <param name="Status">结局。</param>
/// <param name="Error">失败原因（仅 <see cref="RevokeStatus.Failed"/> 有值）。</param>
/// <param name="ViaFingerprint">是否走了「内容指纹」兜底路径（用于日志区分）。</param>
internal sealed record RevokeResult(RevokeStatus Status, string? Error, bool ViaFingerprint = false);

/// <summary>
/// 「删除即吊销」：删除历史记录时，如果当前系统剪贴板里装的正是这一条，就顺手把它清空，
/// 于是桌面右键的「粘贴」立刻变灰 —— 删掉的东西不会再被误粘贴出来（2026-09-19 用户需求）。
/// <para>
/// 安全边界（必须严格遵守，否则会毁掉用户刚复制的内容）：
/// ① <b>只有内容确实是我们这一条才清</b>：先查记账本（本次运行内捕获/写回过的内容），
///    记账本给不出答案时再按内容指纹重新认一次身份；
/// ② <b>判定与清空必须原子</b>：清空走 <see cref="ClipboardAccess.TryClearIfSequence"/>，
///    它在持有剪贴板锁期间复核序列号，序列号变了就放弃（说明用户已经复制了新东西）；
/// ③ <b>只清当前剪贴板，不碰别处</b>：Win+V 历史里的副本、已经粘贴出去的内容、
///    其它剪贴板管理器的数据库都动不了（系统没有提供按条目删除的接口）。
/// </para>
/// <para>线程要求：剪贴板读写必须在 STA 线程，本类所有方法都只能在 UI 线程调用。</para>
/// </summary>
internal sealed class ClipboardRevoker
{
    private readonly ClipboardAccess _clipboard;
    private readonly ClipboardPresenceTracker _presence;
    private readonly AppLog _log;
    private readonly Func<string, bool> _isKnownHash;

    /// <summary>创建吊销器。</summary>
    /// <param name="clipboard">剪贴板访问器（STA）。</param>
    /// <param name="presence">剪贴板身份记账本。</param>
    /// <param name="log">日志器。</param>
    /// <param name="isKnownHash">按内容哈希查历史是否仍有该记录（供「清空历史」前的判定使用）。</param>
    public ClipboardRevoker(
        ClipboardAccess clipboard,
        ClipboardPresenceTracker presence,
        AppLog log,
        Func<string, bool> isKnownHash)
    {
        _clipboard = clipboard;
        _presence = presence;
        _log = log;
        _isKnownHash = isKnownHash;
    }

    /// <summary>
    /// 删除单条记录后调用：仅当剪贴板里正是这条记录时清空它。
    /// </summary>
    /// <param name="deleted">刚被删除的记录（已不在库里，用它的哈希/主键做判定）。</param>
    public RevokeResult RevokeIfMatches(ClipItem deleted)
    {
        ArgumentNullException.ThrowIfNull(deleted);

        // 快速路径：记账本确凿（同一会话内捕获过或我们写回过），连剪贴板都不用读。
        var live = ClipboardAccess.GetSequenceNumber();
        if (_presence.IsCurrent(deleted.Id, live))
        {
            _log.Diag($"吊销判定：记账命中（id={deleted.Id} 序列号={live}）");
            return Clear(live, viaFingerprint: false);
        }

        _log.Diag(
            $"吊销判定：记账未命中（id={deleted.Id} 实时序列号={live} 记账条目={_presence.ItemId?.ToString() ?? "无"}），改用内容指纹");

        // 兜底路径：按内容指纹重新认身份（覆盖「重启后记账本为空」「内容被重新复制过」等情况）。
        if (!_clipboard.TryReadPayloadWithSequence(out var payload, out var readSequence, out var readError))
        {
            _log.Diag("吊销判定：读剪贴板失败或无受支持格式（" + (readError ?? "无格式") + "）");
            return readError is null
                ? new RevokeResult(RevokeStatus.NotCurrent, null)
                : new RevokeResult(RevokeStatus.Failed, readError);
        }

        if (payload is null)
        {
            return new RevokeResult(RevokeStatus.NotCurrent, null);
        }

        var fingerprint = ClipboardFingerprint.Of(payload);
        var matched = fingerprint is not null
            && string.Equals(fingerprint, deleted.ContentHash, StringComparison.Ordinal);

        _log.Diag(
            $"吊销判定：内容指纹 类型={payload.Type} 命中={matched}（记录={ShortHash(deleted.ContentHash)} 当前={ShortHash(fingerprint)} 序列号={readSequence}）");

        if (!matched)
        {
            return new RevokeResult(RevokeStatus.NotCurrent, null);
        }

        return Clear(readSequence, viaFingerprint: true);
    }

    /// <summary>
    /// 「清空历史」删除<b>之前</b>的判定：当前剪贴板里装的是不是我们历史里的某条记录
    /// （记录一删就再也认不出身份了，所以必须先问）。
    /// </summary>
    /// <param name="sequence">判定时该内容对应的序列号，供随后按条件清空使用。</param>
    public bool HoldsTrackedRecord(out long sequence)
    {
        sequence = 0;

        var live = ClipboardAccess.GetSequenceNumber();
        if (_presence.HoldsTrackedItem(live))
        {
            sequence = live;
            return true;
        }

        if (!_clipboard.TryReadPayloadWithSequence(out var payload, out var readSequence, out _) || payload is null)
        {
            return false;
        }

        var fingerprint = ClipboardFingerprint.Of(payload);
        if (fingerprint is null || !_isKnownHash(fingerprint))
        {
            return false;
        }

        sequence = readSequence;
        return true;
    }

    /// <summary>序列号未变才清空（「清空历史」删除完成后调用）。</summary>
    /// <param name="sequence"><see cref="HoldsTrackedRecord"/> 返回的序列号。</param>
    public RevokeResult ClearIfUnchanged(long sequence) => Clear(sequence, viaFingerprint: true);

    /// <summary>哈希只用于日志对照：截断到前 8 位，<b>绝不记录内容本身</b>（AGENTS.md §4）。</summary>
    private static string ShortHash(string? hash) => hash is null ? "无" : hash[..Math.Min(8, hash.Length)];

    private RevokeResult Clear(long expectedSequence, bool viaFingerprint)
    {
        var result = _clipboard.TryClearIfSequence(expectedSequence);
        var route = viaFingerprint ? "内容指纹" : "记账命中";

        switch (result.Status)
        {
            case ClipboardClearStatus.Cleared:
                _presence.Reset();
                _log.Info($"删除即吊销：已清空系统剪贴板（判定路径={route}）");
                return new RevokeResult(RevokeStatus.Revoked, null, viaFingerprint);

            case ClipboardClearStatus.SequenceChanged:
                // 判定之后剪贴板已被别的程序改写：宁可漏清，也不能毁掉用户的新内容。
                _log.Diag($"删除即吊销：判定后剪贴板已变化（路径={route}），保持不动");
                return new RevokeResult(RevokeStatus.NotCurrent, null, viaFingerprint);

            default:
                _log.Error("删除即吊销失败：" + result.Error);
                return new RevokeResult(RevokeStatus.Failed, result.Error, viaFingerprint);
        }
    }
}
