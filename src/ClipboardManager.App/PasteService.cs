using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Text;
using ClipboardManager.Interop;
using ClipboardManager.Storage;

namespace ClipboardManager.App;

/// <summary>
/// 粘贴第一步：把记录<b>原文</b>写回剪贴板，并登记「这是本进程自己写的」，
/// 供自循环过滤器在随后到来的 <c>WM_CLIPBOARDUPDATE</c> 中跳过（需求 §5.1）。
/// </summary>
internal sealed class PasteService
{
    private readonly ClipboardAccess _clipboard;
    private readonly SelfWriteFilter _selfWrite;
    private readonly AppLog _log;

    /// <summary>创建服务。</summary>
    /// <param name="clipboard">剪贴板访问器（必须在 STA 线程上调用）。</param>
    /// <param name="selfWrite">自循环过滤器。</param>
    /// <param name="log">日志器。</param>
    public PasteService(ClipboardAccess clipboard, SelfWriteFilter selfWrite, AppLog log)
    {
        _clipboard = clipboard;
        _selfWrite = selfWrite;
        _log = log;
    }

    /// <summary>
    /// 把记录内容写回剪贴板。
    /// </summary>
    /// <param name="item">目标记录（使用其原文 <c>TextContent</c>，绝不使用脱敏后的展示文本）。</param>
    /// <param name="error">失败原因。</param>
    public bool TryCopyToClipboard(ClipItem item, out string? error)
    {
        ArgumentNullException.ThrowIfNull(item);
        error = null;

        var text = item.TextContent ?? item.Preview;
        if (!_clipboard.TryWriteText(text, out var sequence, out error))
        {
            _log.Error("写回剪贴板失败：" + error);
            return false;
        }

        _selfWrite.NoteOwnWrite(sequence, ContentHasher.ForText(text), DateTimeOffset.Now);
        return true;
    }
}
