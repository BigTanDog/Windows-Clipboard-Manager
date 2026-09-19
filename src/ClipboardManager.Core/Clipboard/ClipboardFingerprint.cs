using ClipboardManager.Core.Html;
using ClipboardManager.Core.Imaging;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Text;

namespace ClipboardManager.Core.Clipboard;

/// <summary>
/// 剪贴板候选的「内容指纹」：<b>与入库去重键（<see cref="ClipItem.ContentHash"/>）完全同一口径</b>的哈希。
/// <para>
/// 用途：判断「当前剪贴板里的内容是否就是历史里的某条记录」。记账本
/// （<see cref="ClipboardPresenceTracker"/>）只能覆盖「本次运行期间捕获过的内容」，
/// 重启后剪贴板里往往还留着上一条内容 —— 这时按下述兜底逻辑，用指纹重新认一次身份，
/// 才能让「删除即吊销」和「剪贴板中」徽标在重启后依然准确。
/// </para>
/// <para>
/// <b>维护约定（重要）</b>：本文件与 <c>App/CaptureProcessor</c> 是同一套哈希口径的两处实现 ——
/// 文本<b>不</b>做任何额外归一化、文件列表先过 <see cref="PathValidator.Filter"/>、
/// HTML 用解析后的片段文本、图片用<b>解码后的 BGRA 像素</b>。
/// 改动其中一处必须同步另一处，并跑 <c>tests/ClipboardManager.Tests/ClipboardRevokeTests</c> 里的
/// 等价性测试（它用真实的 <c>CaptureProcessor</c> 交叉验证两者结果一致），否则会静默失效。
/// </para>
/// </summary>
public static class ClipboardFingerprint
{
    /// <summary>
    /// 计算候选的内容指纹；返回 null 表示该候选无法与历史记录对应（解析失败等）。
    /// </summary>
    /// <param name="candidate">剪贴板候选（纯托管数据）。</param>
    public static string? Of(ClipCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return candidate.Type switch
        {
            ClipContentType.Text => ContentHasher.ForText(candidate.Text ?? string.Empty),
            ClipContentType.FileList => ForFileList(candidate.FilePaths),
            ClipContentType.Html => ForHtml(candidate.Binary),
            ClipContentType.Image => ForImage(candidate.Binary),
            _ => null,
        };
    }

    private static string? ForFileList(IReadOnlyList<string> rawPaths)
    {
        if (rawPaths.Count == 0)
        {
            return null;
        }

        // 与捕获路径一致：先校验/规范化（纯字符串处理，不读文件），顺序保持不变。
        var paths = PathValidator.Filter(rawPaths, out _);
        return paths.Count == 0 ? null : ContentHasher.ForFileList(paths);
    }

    private static string? ForHtml(byte[]? raw)
    {
        if (raw is not { Length: > 0 })
        {
            return null;
        }

        return HtmlClipboardParser.TryParse(raw, out var parsed, out _) && parsed is not null
            ? ContentHasher.ForText(parsed.Html)
            : null;
    }

    private static string? ForImage(byte[]? dibBytes)
    {
        if (dibBytes is not { Length: > 0 })
        {
            return null;
        }

        // 基于规范化像素（解码后 BGRA32、去行填充）：同图不同 DIB 头也算同一条（与捕获一致）。
        return DibParser.TryParse(dibBytes, out var image, out _) && image is not null
            ? ContentHasher.ForBinary(image.BgraPixels)
            : null;
    }
}
