using ClipboardManager.App.Imaging;
using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Html;
using ClipboardManager.Core.Imaging;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Text;
using ClipboardManager.Storage;

namespace ClipboardManager.App;

/// <summary>处理结果：交给仓储入库所需的一切（原文候选 + 哈希 + 摘要 + 本体路径 + 体积）。</summary>
/// <param name="Candidate">处理后的候选（文本为派生纯文本、文件路径已校验）。</param>
/// <param name="ContentHash">去重键。</param>
/// <param name="Preview">原文摘要（显示时再脱敏）。</param>
/// <param name="BlobPath">本体相对路径（图片 / HTML）。</param>
/// <param name="SizeBytes">入库体积。</param>
internal sealed record ProcessedCapture(
    ClipCandidate Candidate,
    string ContentHash,
    string Preview,
    string? BlobPath,
    long SizeBytes);

/// <summary>
/// 剪贴板候选的后台处理器：解析 / 归一化 / 哈希 / 编码 / 落本体文件。
/// <para>
/// 全部在后台线程执行（AGENTS.md §3）：入参是已经拷贝到托管内存的纯数据，
/// 绝不含任何 Win32 句柄。
/// </para>
/// </summary>
internal sealed class CaptureProcessor
{
    private readonly BlobStore _blobs;
    private readonly AppLog _log;
    private readonly bool _captureImages;

    /// <summary>创建处理器。</summary>
    /// <param name="blobs">本体存储。</param>
    /// <param name="log">日志器。</param>
    /// <param name="captureImages">设置项：是否记录图片。</param>
    public CaptureProcessor(BlobStore blobs, AppLog log, bool captureImages)
    {
        _blobs = blobs;
        _log = log;
        _captureImages = captureImages;
    }

    /// <summary>
    /// 处理一个候选；返回 null 表示按规则应丢弃（原因已记日志）。
    /// </summary>
    /// <param name="candidate">剪贴板候选。</param>
    public ProcessedCapture? Process(ClipCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return candidate.Type switch
        {
            ClipContentType.Text => ProcessText(candidate),
            ClipContentType.FileList => ProcessFileList(candidate),
            ClipContentType.Html => ProcessHtml(candidate),
            ClipContentType.Image => ProcessImage(candidate),
            _ => null,
        };
    }

    private ProcessedCapture ProcessText(ClipCandidate candidate)
    {
        var text = candidate.Text ?? string.Empty;
        return new ProcessedCapture(candidate, ContentHasher.ForText(text), PreviewBuilder.ForText(text), null, candidate.SizeBytes);
    }

    private ProcessedCapture? ProcessFileList(ClipCandidate candidate)
    {
        var paths = PathValidator.Filter(candidate.FilePaths, out var rejected);
        if (rejected > 0)
        {
            _log.Diag($"文件列表：丢弃 {rejected} 条非法/重复路径");
        }

        if (paths.Count == 0)
        {
            _log.Diag("文件列表：过滤后无有效路径，忽略");
            return null;
        }

        var effective = candidate with { FilePaths = paths };
        return new ProcessedCapture(
            effective,
            ContentHasher.ForFileList(paths),
            PreviewBuilder.ForFileList(paths),
            null,
            effective.SizeBytes);
    }

    private ProcessedCapture? ProcessHtml(ClipCandidate candidate)
    {
        if (candidate.Binary is not { Length: > 0 } raw)
        {
            return null;
        }

        if (!HtmlClipboardParser.TryParse(raw, out var parsed, out var error) || parsed is null)
        {
            _log.Diag("HTML 解析失败，忽略：" + error);
            return null;
        }

        // 存 HTML 片段本体（元数据与二进制分离），文本内容用派生纯文本（供搜索与写回降级）。
        var fragmentBytes = System.Text.Encoding.UTF8.GetBytes(parsed.Html);
        var hash = ContentHasher.ForText(parsed.Html);
        var blobPath = _blobs.Save(fragmentBytes, hash, "html");
        var effective = candidate with { Text = parsed.PlainText, Binary = null };

        return new ProcessedCapture(
            effective,
            hash,
            PreviewBuilder.ForText(parsed.PlainText),
            blobPath,
            fragmentBytes.LongLength);
    }

    private ProcessedCapture? ProcessImage(ClipCandidate candidate)
    {
        if (!_captureImages)
        {
            _log.Diag("图片：设置项已关闭记录图片，忽略");
            return null;
        }

        if (candidate.Binary is not { Length: > 0 } dibBytes)
        {
            return null;
        }

        if (!DibParser.TryParse(dibBytes, out var image, out var error) || image is null)
        {
            _log.Diag("图片解析失败，忽略：" + error);
            return null;
        }

        // 哈希基于「规范化像素」（解码后 BGRA32、去行填充），避免同图不同 DIB 头被判为不同内容。
        var hash = ContentHasher.ForBinary(image.BgraPixels);

        var png = PngEncoder.EncodeBgra(image.Width, image.Height, image.BgraPixels);
        var blobPath = _blobs.Save(png, hash, "png");

        var thumbnail = PngEncoder.TryCreateThumbnail(png);
        if (thumbnail is not null)
        {
            // 缩略图与本体同目录，供列表快速渲染（加载时不锁文件）。
            _ = _blobs.Save(thumbnail, hash, "png", thumbnail: true);
        }

        var effective = candidate with { Binary = null };
        return new ProcessedCapture(
            effective,
            hash,
            PreviewBuilder.ForImage(image.Width, image.Height, png.LongLength),
            blobPath,
            png.LongLength);
    }
}
