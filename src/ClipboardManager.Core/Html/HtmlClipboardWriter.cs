using System.Text;

namespace ClipboardManager.Core.Html;

/// <summary>
/// 构造 <c>"HTML Format"</c> 剪贴板数据（D-08：粘贴 HTML 记录时写回的数据）。
/// <para>
/// 头部五个字段的位置必须精确：<c>StartFragment</c> 指向 <c>&lt;!--StartFragment--&gt;</c>
/// 之后、<c>EndFragment</c> 指向 <c>&lt;!--EndFragment--&gt;</c> 之前，且所有偏移都是
/// <b>UTF-8 字节偏移</b>（含头部自身）。字段宽度固定 10 位十进制，便于稳定计算。
/// </para>
/// </summary>
public static class HtmlClipboardWriter
{
    /// <summary>
    /// 由 HTML 片段构造完整的剪贴板字节（UTF-8）。
    /// </summary>
    /// <param name="fragment">纯 HTML 片段（不含头部）。</param>
    public static byte[] Build(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        // 先按「占位头部」计算体长，再回填偏移。
        const string emptyHeader = "Version:0.9\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\n"
            + "StartFragment:0000000000\r\nEndFragment:0000000000\r\n";
        var headerBytes = Encoding.UTF8.GetByteCount(emptyHeader);

        const string bodyPrefix = "<html><body>\r\n<!--StartFragment-->";
        const string bodySuffix = "<!--EndFragment-->\r\n</body></html>";

        var prefixBytes = Encoding.UTF8.GetByteCount(bodyPrefix);
        var fragmentBytes = Encoding.UTF8.GetByteCount(fragment);
        var suffixBytes = Encoding.UTF8.GetByteCount(bodySuffix);

        var startHtml = headerBytes;
        var startFragment = startHtml + prefixBytes;
        var endFragment = startFragment + fragmentBytes;
        var endHtml = endFragment + suffixBytes;

        var header = "Version:0.9\r\n"
            + $"StartHTML:{startHtml:0000000000}\r\n"
            + $"EndHTML:{endHtml:0000000000}\r\n"
            + $"StartFragment:{startFragment:0000000000}\r\n"
            + $"EndFragment:{endFragment:0000000000}\r\n";

        // 头部长度必须与占位时一致（宽度固定，所以恒等）。
        if (Encoding.UTF8.GetByteCount(header) != headerBytes)
        {
            throw new InvalidOperationException("HTML 头长度计算不一致");
        }

        return Encoding.UTF8.GetBytes(header + bodyPrefix + fragment + bodySuffix);
    }
}
