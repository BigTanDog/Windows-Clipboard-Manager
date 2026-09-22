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

    /// <summary>创建处理器。</summary>
    /// <param name="blobs">本体存储。</param>
    /// <param name="log">日志器。</param>
    /// <param name="captureImages">设置项：是否记录图片。</param>
    public CaptureProcessor(BlobStore blobs, AppLog log, bool captureImages)
    {
        _blobs = blobs;
        _log = log;
        CaptureImages = captureImages;
    }

    /// <summary>设置项：是否记录图片（可在设置保存后热更新，无需重启）。</summary>
    public bool CaptureImages { get; set; }

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
        var hash = ContentHasher.ForFileList(paths);
        var (blobPath, sizeBytes) = CreateImageFileBlob(paths, hash, effective.SizeBytes);

        return new ProcessedCapture(
            effective,
            hash,
            PreviewBuilder.ForFileList(paths),
            blobPath,
            sizeBytes);
    }

    /// <summary>
    /// 复制的是**单个图片文件**时，生成一张缩略图作为该记录的本体（用户 2026-09-21 需求：
    /// 列表里能像截图那样直接看到图）。
    /// <para>
    /// 两个刻意的取舍：① <b>不缓存原文件内容</b>（"文件内容缓存"是产品计划 §1.2 的永久非目标），
    /// 只存一张长边不超过 256px 的 PNG；② <b>记录类型仍是"文件"</b>，粘贴行为不变 ——
    /// 粘出去的仍然是文件本身（若改成图片类型，粘到资源管理器里就会变成图片内容，是错误行为）。
    /// </para>
    /// <para>生成失败（格式不支持 / 文件已被移动 / 太大）就当作普通文件记录，不报错、不影响入库。</para>
    /// </summary>
    /// <param name="paths">已校验的文件路径列表。</param>
    /// <param name="hash">记录的内容哈希（仍是文件列表哈希，去重口径不变）。</param>
    /// <param name="fallbackSize">没有缩略图时的记录体积。</param>
    private (string? BlobPath, long SizeBytes) CreateImageFileBlob(
        IReadOnlyList<string> paths,
        string hash,
        long fallbackSize)
    {
        if (ImageFileExtensions.TryGetSingleImageFile(paths) is not { } filePath)
        {
            return (null, fallbackSize);
        }

        var thumbnail = PngEncoder.TryCreateThumbnailFromFile(filePath);
        if (thumbnail is null)
        {
            _log.Diag("图片文件：缩略图生成失败（格式不支持 / 文件已移动 / 过大），按普通文件显示");
            return (null, fallbackSize);
        }

        try
        {
            // 本体路径内容寻址（同哈希同扩展名）：本体就是这张缩略图，
            // 删除、孤儿清理、磁盘占用统计全都沿用现有机制，不需要任何特殊分支。
            var blobPath = _blobs.Save(thumbnail, hash, "png");
            return (blobPath, thumbnail.LongLength);
        }
        catch (Exception ex)
        {
            // 写盘失败绝不能连累记录入库：退化成"没有缩略图的文件记录"。
            _log.Error("保存图片文件缩略图失败", ex);
            return (null, fallbackSize);
        }
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

        // 识别优化（用户 2026-09-22 反馈）：在浏览器里选中一段文字复制时，剪贴板**同时**带
        // 「HTML Format」与纯文本，而读取优先级是先 HTML —— 于是"复制一句话"被存成 HTML 记录、
        // 列表里显示「HTML」徽标，可用户明明复制的是文字。
        // 判定：片段里没有任何结构/语义元素（链接 / 图片 / 列表 / 表格 / 代码 / 换行 / 段落）时，
        // 它本质上就是"带样式的文字" → 按**纯文本**入库（粘贴出去的也是纯文本，正是用户的预期）。
        // 纯样式（span / font / b / strong / h1~h6 的颜色字体字号）不算结构，浏览器选区正是这种形态。
        if (!HtmlStructure.HasStructuralElement(parsed.Html) && !string.IsNullOrWhiteSpace(parsed.PlainText))
        {
            var textOnly = candidate with
            {
                Type = ClipContentType.Text,
                Text = parsed.PlainText,
                Binary = null,
                BlobExtension = null,
            };

            _log.Diag("HTML 片段没有结构元素（纯排版样式），按纯文本入库");

            return new ProcessedCapture(
                textOnly,
                ContentHasher.ForText(parsed.PlainText),
                PreviewBuilder.ForText(parsed.PlainText),
                null,
                textOnly.SizeBytes);
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
        if (!CaptureImages)
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
