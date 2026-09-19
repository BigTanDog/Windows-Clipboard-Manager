using ClipboardManager.App.Imaging;
using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Imaging;
using ClipboardManager.Core.Models;
using ClipboardManager.Interop;
using ClipboardManager.Storage;

namespace ClipboardManager.App;

/// <summary>
/// 粘贴第一步：把记录<b>原文</b>按类型写回剪贴板，并登记「这是本进程自己写的」，
/// 供自循环过滤器跳过随后到来的 <c>WM_CLIPBOARDUPDATE</c>（需求 §5.1）。
/// </summary>
internal sealed class PasteService
{
    private readonly ClipboardAccess _clipboard;
    private readonly SelfWriteFilter _selfWrite;
    private readonly BlobStore _blobs;
    private readonly AppLog _log;

    /// <summary>创建服务。</summary>
    /// <param name="clipboard">剪贴板访问器（必须在 STA 线程上调用）。</param>
    /// <param name="selfWrite">自循环过滤器。</param>
    /// <param name="blobs">本体存储（图片 / HTML 记录需要读回本体）。</param>
    /// <param name="log">日志器。</param>
    public PasteService(ClipboardAccess clipboard, SelfWriteFilter selfWrite, BlobStore blobs, AppLog log)
    {
        _clipboard = clipboard;
        _selfWrite = selfWrite;
        _blobs = blobs;
        _log = log;
    }

    /// <summary>
    /// 把记录内容写回剪贴板（按类型选择格式；HTML 记录额外附纯文本降级，D-08）。
    /// </summary>
    /// <param name="item">目标记录（使用原文，绝不使用脱敏后的展示文本）。</param>
    /// <param name="sequenceAfterWrite">写入后的剪贴板序列号（供调用方登记「剪贴板里就是这条」）。</param>
    /// <param name="error">失败原因。</param>
    public bool TryCopyToClipboard(ClipItem item, out long sequenceAfterWrite, out string? error)
    {
        ArgumentNullException.ThrowIfNull(item);
        sequenceAfterWrite = 0;
        error = null;

        var request = BuildRequest(item);
        if (request is null)
        {
            error = $"记录类型 {item.Type} 缺少可写回的内容（本体文件可能已被删除）";
            _log.Error("写回剪贴板失败：" + error);
            return false;
        }

        if (!_clipboard.TryWrite(request, out var sequence, out error))
        {
            _log.Error("写回剪贴板失败：" + error);
            return false;
        }

        sequenceAfterWrite = sequence;
        _selfWrite.NoteOwnWrite(sequence, request.ContentHash, DateTimeOffset.Now);
        return true;
    }

    private ClipboardWriteRequest? BuildRequest(ClipItem item) => item.Type switch
    {
        ClipContentType.Text => new ClipboardWriteRequest
        {
            Text = item.TextContent ?? item.Preview,
            ContentHash = item.ContentHash,
        },

        ClipContentType.FileList => item.FilePaths.Count > 0
            ? new ClipboardWriteRequest { FilePaths = item.FilePaths, ContentHash = item.ContentHash }
            : null,

        ClipContentType.Html => BuildHtmlRequest(item),

        ClipContentType.Image => BuildImageRequest(item),

        _ => null,
    };

    /// <summary>HTML：写回 "HTML Format" + 纯文本（记事本 / 终端等只认纯文本的程序也能粘贴）。</summary>
    private ClipboardWriteRequest? BuildHtmlRequest(ClipItem item)
    {
        var blob = _blobs.TryRead(item.BlobPath);
        if (blob is null)
        {
            return null;
        }

        var fragment = System.Text.Encoding.UTF8.GetString(blob);
        return new ClipboardWriteRequest
        {
            Html = fragment,
            Text = item.TextContent ?? string.Empty,
            ContentHash = item.ContentHash,
        };
    }

    /// <summary>图片：PNG 本体 → BGRA32 像素 → CF_DIBV5（带 alpha，主流程序均能识别）。</summary>
    private ClipboardWriteRequest? BuildImageRequest(ClipItem item)
    {
        var png = _blobs.TryRead(item.BlobPath);
        if (png is null)
        {
            return null;
        }

        if (!PngEncoder.TryDecodeToBgra(png, out var width, out var height, out var bgra))
        {
            _log.Error("图片解码失败，无法写回剪贴板");
            return null;
        }

        return new ClipboardWriteRequest
        {
            DibV5 = DibWriter.BuildDibV5(width, height, bgra),
            ContentHash = item.ContentHash,
        };
    }
}
