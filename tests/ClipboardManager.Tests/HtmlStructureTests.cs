using System.IO;
using ClipboardManager.App;
using ClipboardManager.Core.Html;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Text;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 「浏览器复制的文字不该被当成 HTML」的判定测试（用户 2026-09-22 反馈）。
/// <para>
/// 用例形态直接取自用户真实历史里的 HTML 本体：浏览器选区 = 一层带 <c>style</c> 的 <c>span</c>，
/// 纯文字连标签都没有；而带链接/列表/换行/表格的片段必须继续保留 HTML（否则会丢信息）。
/// </para>
/// </summary>
public sealed class HtmlStructureTests : IDisposable
{
    private readonly string _directory;

    public HtmlStructureTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "clipboard-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 忽略清理失败。
        }
    }

    // ────────────────────── 判定：只是"带样式的文字" ──────────────────────

    [Theory]
    [InlineData("朝晖")]
    [InlineData("有节奏才证明是好二游啊")]
    [InlineData("<html><body>@huangyou_A</body></html>")]
    [InlineData("<span style=\"color: rgba(250, 251, 255, 0.7); font-family: &quot;Microsoft YaHei&quot;; font-size: 16px;\">一段普通文字</span>")]
    [InlineData("<b>加粗</b>")]
    [InlineData("<strong style=\"font-weight: 600\">加粗段落</strong>")]
    [InlineData("<h2>一个标题</h2>")]
    [InlineData("<em>斜体</em><u>下划线</u><s>删除线</s>")]
    [InlineData("<!-- 注释 --><span>只有注释和文字</span>")]
    public void 纯样式片段可按文本入库(string html)
    {
        Assert.False(HtmlStructure.HasStructuralElement(html));
    }

    // ────────────────────── 判定：必须保留 HTML（降级会丢信息） ──────────────────────

    [Theory]
    [InlineData("<a href=\"https://example.com\">链接</a>")]
    [InlineData("<span>看这个</span><a href=\"https://x\">站</a>")]
    [InlineData("<p class=\"ds-markdown-paragraph\" style=\"margin: 16px 0\">一个段落</p>")]
    [InlineData("<div>第一段</div><div>第二段</div>")]
    [InlineData("<span>第一行</span><br><span>第二行</span>")]
    [InlineData("<ul><li>项一</li><li>项二</li></ul>")]
    [InlineData("<table><tr><td>单元格</td></tr></table>")]
    [InlineData("<img src=\"a.png\">")]
    [InlineData("<pre>code block</pre>")]
    [InlineData("<hr>")]
    [InlineData("<P>大写标签同样算结构</P>")]
    public void 带结构或语义的片段必须保留HTML(string html)
    {
        Assert.True(HtmlStructure.HasStructuralElement(html));
    }

    [Fact]
    public void 空片段没有结构元素()
    {
        Assert.False(HtmlStructure.HasStructuralElement(null));
        Assert.False(HtmlStructure.HasStructuralElement(string.Empty));
    }

    // ────────────────────── 端到端：入库类型 ──────────────────────

    [Fact]
    public void 浏览器复制的文字会按纯文本入库()
    {
        var processor = NewProcessor();
        const string fragment = "<span style=\"font-family: &quot;Microsoft YaHei&quot;; font-size: 16px;\">今天天气不错</span>";

        var processed = processor.Process(HtmlCandidate(fragment));

        Assert.NotNull(processed);
        Assert.Equal(ClipContentType.Text, processed!.Candidate.Type);   // 不再是 HTML
        Assert.Null(processed.BlobPath);                                 // 也不存 HTML 本体
        Assert.Equal("今天天气不错", processed.Candidate.Text);
        Assert.Equal(ContentHasher.ForText("今天天气不错"), processed.ContentHash);
        Assert.Equal("文本", PreviewBuilder.TypeLabel(processed.Candidate.Type));
    }

    [Fact]
    public void 带链接的片段仍然按HTML入库()
    {
        var processor = NewProcessor();
        const string fragment = "<a href=\"https://example.com\">去看这个</a>";

        var processed = processor.Process(HtmlCandidate(fragment));

        Assert.NotNull(processed);
        Assert.Equal(ClipContentType.Html, processed!.Candidate.Type);
        Assert.NotNull(processed.BlobPath);
        Assert.Equal("去看这个", processed.Candidate.Text);
    }

    [Fact]
    public void 多段落片段保留HTML以免丢行结构()
    {
        // 派生纯文本会把空白折叠成单空格，所以"有段落/换行"的片段不能降级，否则行结构就丢了。
        var processor = NewProcessor();
        const string fragment = "<div>第一行</div><div>第二行</div>";

        var processed = processor.Process(HtmlCandidate(fragment));

        Assert.NotNull(processed);
        Assert.Equal(ClipContentType.Html, processed!.Candidate.Type);
        Assert.NotNull(processed.BlobPath);
    }

    // ────────────────────── 夹具 ──────────────────────

    private CaptureProcessor NewProcessor() =>
        new(new BlobStore(_directory), new AppLog(null), captureImages: true);

    private static ClipCandidate HtmlCandidate(string fragment) => new()
    {
        Type = ClipContentType.Html,
        Binary = HtmlClipboardWriter.Build(fragment),
        BlobExtension = "html",
        CapturedAt = DateTimeOffset.Now,
    };
}
