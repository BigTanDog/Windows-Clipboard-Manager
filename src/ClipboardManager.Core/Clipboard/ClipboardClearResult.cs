namespace ClipboardManager.Core.Clipboard;

/// <summary>条件清空剪贴板的结局。</summary>
public enum ClipboardClearStatus
{
    /// <summary>已清空：剪贴板此刻为空（所有权转到本进程）。</summary>
    Cleared,

    /// <summary>未清空：序列号已变化 —— 内容在判定之后被换掉了，<b>绝不能清</b>（否则会毁掉用户刚复制的东西）。</summary>
    SequenceChanged,

    /// <summary>未清空：打不开剪贴板（被其它程序占用）等失败情形。</summary>
    Failed,
}

/// <summary>
/// <see cref="ClipboardClearStatus"/> 的结果载体。
/// </summary>
/// <param name="Status">结局。</param>
/// <param name="SequenceAfter">清空后的新序列号（仅 <see cref="ClipboardClearStatus.Cleared"/> 有意义）。</param>
/// <param name="Error">失败原因（仅 <see cref="ClipboardClearStatus.Failed"/> 有值）。</param>
public sealed record ClipboardClearResult(ClipboardClearStatus Status, long SequenceAfter, string? Error)
{
    /// <summary>是否真的清空了。</summary>
    public bool IsCleared => Status == ClipboardClearStatus.Cleared;
}
