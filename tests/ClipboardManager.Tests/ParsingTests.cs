using System.Buffers.Binary;
using System.Text;
using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Html;
using ClipboardManager.Core.Imaging;

namespace ClipboardManager.Tests;

/// <summary>DIB 解析 / 回写测试（技术设计 §5.2）。全部使用构造数据，覆盖畸形样本。</summary>
public class DibParserTests
{
    [Fact]
    public void 三十二位自上而下_像素原样保留()
    {
        var pixels = new byte[]
        {
            1, 2, 3, 255, 4, 5, 6, 255,
            7, 8, 9, 255, 10, 11, 12, 255,
        };

        var dib = BuildDib(2, 2, 32, topDown: true, pixels);

        Assert.True(DibParser.TryParse(dib, out var image, out var error), error);
        Assert.NotNull(image);
        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(pixels, image.BgraPixels);
    }

    [Fact]
    public void 三十二位自下而上_行序被翻转()
    {
        var stored = new byte[]
        {
            1, 1, 1, 255, 2, 2, 2, 255, // 存储的第一行 = 图像最后一行
            3, 3, 3, 255, 4, 4, 4, 255,
        };

        var dib = BuildDib(2, 2, 32, topDown: false, stored);

        Assert.True(DibParser.TryParse(dib, out var image, out var error), error);
        Assert.NotNull(image);

        // 期望自上而下为：第 3,4 像素 在前，1,2 在后
        Assert.Equal(new byte[] { 3, 3, 3, 255, 4, 4, 4, 255, 1, 1, 1, 255, 2, 2, 2, 255 }, image.BgraPixels);
    }

    [Fact]
    public void 二十四位_扩展为BGRA且不透明()
    {
        // 2 像素：BGR
        var dib = BuildDib(2, 1, 24, topDown: true, [1, 2, 3, 4, 5, 6]);

        Assert.True(DibParser.TryParse(dib, out var image, out var error), error);
        Assert.NotNull(image);
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, image.BgraPixels);
    }

    [Fact]
    public void V5头_标准掩码且alpha全零_按不透明处理()
    {
        var pixels = new byte[] { 10, 20, 30, 0, 40, 50, 60, 0 };
        var dib = BuildDibV5WithMasks(2, 1, pixels);

        Assert.True(DibParser.TryParse(dib, out var image, out var error), error);
        Assert.NotNull(image);
        Assert.Equal(255, image.BgraPixels[3]);
        Assert.Equal(255, image.BgraPixels[7]);
    }

    [Fact]
    public void V5头_非标准掩码_拒绝()
    {
        var dib = BuildDibV5WithMasks(1, 1, [1, 2, 3, 255], redMask: 0x000000FF);

        Assert.False(DibParser.TryParse(dib, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(0)]   // 数据长度不足
    [InlineData(39)]
    public void 长度不足_拒绝(int length)
    {
        Assert.False(DibParser.TryParse(new byte[length], out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void 不支持的头大小_拒绝()
    {
        var dib = BuildDib(1, 1, 32, topDown: true, [1, 2, 3, 255]);
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 12); // BITMAPCOREHEADER

        Assert.False(DibParser.TryParse(dib, out _, out var error));
        Assert.Contains("头大小", error, StringComparison.Ordinal);
    }

    [Fact]
    public void 不支持的位深_拒绝()
    {
        var dib = BuildDib(1, 1, 32, topDown: true, [1, 2, 3, 255]);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 16);

        Assert.False(DibParser.TryParse(dib, out _, out var error));
        Assert.Contains("16", error, StringComparison.Ordinal);
    }

    [Fact]
    public void 宽高非法_拒绝()
    {
        var dib = BuildDib(1, 1, 32, topDown: true, [1, 2, 3, 255]);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 0);

        Assert.False(DibParser.TryParse(dib, out _, out _));

        var zeroHeight = BuildDib(1, 1, 32, topDown: true, [1, 2, 3, 255]);
        BinaryPrimitives.WriteInt32LittleEndian(zeroHeight.AsSpan(8), 0);
        Assert.False(DibParser.TryParse(zeroHeight, out _, out _));
    }

    [Fact]
    public void 像素被截断_拒绝()
    {
        var dib = BuildDib(4, 4, 32, topDown: true, new byte[4 * 16]);
        Array.Resize(ref dib, dib.Length - 8);

        Assert.False(DibParser.TryParse(dib, out _, out var error));
        Assert.Contains("截断", error, StringComparison.Ordinal);
    }

    [Fact]
    public void 不支持的压缩方式_拒绝()
    {
        var dib = BuildDib(1, 1, 32, topDown: true, [1, 2, 3, 255]);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 4); // JPEG

        Assert.False(DibParser.TryParse(dib, out _, out var error));
        Assert.Contains("压缩", error, StringComparison.Ordinal);
    }

    /// <summary>构造 BITMAPINFOHEADER 的 24/32 位 DIB（BI_RGB）。</summary>
    private static byte[] BuildDib(int width, int height, int bitCount, bool topDown, byte[] pixels)
    {
        var stride = ((width * bitCount + 31) / 32) * 4;
        var header = new byte[DibParser.SizeBitmapInfoHeader];
        BinaryPrimitives.WriteUInt32LittleEndian(header, DibParser.SizeBitmapInfoHeader);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), topDown ? -height : height);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)bitCount);

        var data = new byte[DibParser.SizeBitmapInfoHeader + (stride * height)];
        header.CopyTo(data, 0);
        pixels.CopyTo(data, DibParser.SizeBitmapInfoHeader);
        return data;
    }

    /// <summary>构造 BITMAPV5HEADER + BI_BITFIELDS 的 DIB，可指定红色掩码以制造非标准布局。</summary>
    private static byte[] BuildDibV5WithMasks(int width, int height, byte[] pixels, uint redMask = 0x00FF0000)
    {
        var stride = width * 4;
        var header = new byte[DibParser.SizeBitmapV5Header];
        BinaryPrimitives.WriteUInt32LittleEndian(header, DibParser.SizeBitmapV5Header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 3);          // BI_BITFIELDS
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), redMask);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(48), 0x000000FF);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(52), 0xFF000000);

        var data = new byte[DibParser.SizeBitmapV5Header + (stride * height)];
        header.CopyTo(data, 0);
        pixels.CopyTo(data, DibParser.SizeBitmapV5Header);
        return data;
    }
}

/// <summary>DIB 回写测试：写出的数据必须能被自家解析器还原（像素一致）。</summary>
public class DibWriterTests
{
    [Fact]
    public void 回写与解析往返_像素一致()
    {
        var pixels = new byte[]
        {
            9, 8, 7, 255, 6, 5, 4, 128,
            3, 2, 1, 0, 12, 13, 14, 255,
        };

        var dib = DibWriter.BuildDibV5(2, 2, pixels);

        Assert.True(DibParser.TryParse(dib, out var image, out var error), error);
        Assert.NotNull(image);
        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(pixels, image.BgraPixels);
    }

    [Fact]
    public void 参数非法_抛异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DibWriter.BuildDibV5(0, 1, new byte[4]));
        Assert.Throws<ArgumentException>(() => DibWriter.BuildDibV5(2, 2, new byte[4]));
    }
}

/// <summary>HTML Format 解析/写回测试（技术设计 §5.3）。</summary>
public class HtmlClipboardTests
{
    [Fact]
    public void 写回再解析_片段一致()
    {
        const string fragment = "<b>Hello</b> 世界 &amp; friends";

        var bytes = HtmlClipboardWriter.Build(fragment);

        Assert.True(HtmlClipboardParser.TryParse(bytes, out var content, out var error), error);
        Assert.NotNull(content);
        Assert.Equal(fragment, content.Html);
        Assert.Equal("Hello 世界 & friends", content.PlainText);
    }

    [Fact]
    public void 头部偏移是字节偏移_多字节字符也正确()
    {
        // 片段里含中文（UTF-8 每字 3 字节），若按字符下标切就会切错。
        var bytes = HtmlClipboardWriter.Build("中文内容测试");

        Assert.True(HtmlClipboardParser.TryParse(bytes, out var content, out _));
        Assert.NotNull(content);
        Assert.Equal("中文内容测试", content.Html);
    }

    [Theory]
    [InlineData("没有头部字段的纯文本")]
    [InlineData("Version:0.9\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\n")]
    public void 缺少或非法偏移_拒绝(string text)
    {
        Assert.False(HtmlClipboardParser.TryParse(Encoding.UTF8.GetBytes(text), out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void 偏移越界_拒绝()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "Version:0.9\r\nStartHTML:0000000010\r\nEndHTML:0000099999\r\nStartFragment:0000000100\r\nEndFragment:0000099999\r\n<html/>");

        Assert.False(HtmlClipboardParser.TryParse(bytes, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void 起止倒置_拒绝()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "Version:0.9\r\nStartHTML:0000000090\r\nEndHTML:0000000050\r\nStartFragment:0000000090\r\nEndFragment:0000000050\r\n<html/>");

        Assert.False(HtmlClipboardParser.TryParse(bytes, out _, out _));
    }

    [Fact]
    public void 仅HTML段可用时回退到HTML段()
    {
        const string body = "<html><body>fallback</body></html>";
        var header = "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000200\r\n";
        var bytes = Encoding.UTF8.GetBytes(header + body);
        // 回填正确的 StartHTML/EndHTML（头长度 + 体长度）
        var headerBytes = Encoding.UTF8.GetByteCount(header);
        var fixedHeader = $"Version:0.9\r\nStartHTML:{headerBytes:0000000000}\r\nEndHTML:{headerBytes + Encoding.UTF8.GetByteCount(body):0000000000}\r\n";
        bytes = Encoding.UTF8.GetBytes(fixedHeader + body);

        Assert.True(HtmlClipboardParser.TryParse(bytes, out var content, out var error), error);
        Assert.NotNull(content);
        Assert.Equal(body, content.Html);
    }

    [Fact]
    public void 空数据_拒绝()
    {
        Assert.False(HtmlClipboardParser.TryParse(ReadOnlySpan<byte>.Empty, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("<p>Hello <b>world</b></p>", "Hello world")]
    [InlineData("<div>a<script>alert(1)</script>b</div>", "a b")]
    [InlineData("<style>p{color:red}</style>text", "text")]
    [InlineData("a&amp;b&lt;c&gt;d&nbsp;e", "a&b<c>d e")]
    [InlineData("&#20013;&#x6587;", "中文")]
    [InlineData("<p>未闭合<script>丢掉剩余", "未闭合")]
    public void 派生纯文本(string html, string expected) =>
        Assert.Equal(expected, HtmlClipboardParser.ToPlainText(html));
}

/// <summary>CF_HDROP 路径校验与 DROPFILES 构造测试。</summary>
public class FilePathClipboardTests
{
    [Fact]
    public void 过滤非法与相对路径()
    {
        var result = PathValidator.Filter(
            ["C:\\Temp\\a.txt", "relative.txt", "C:\\Temp\\bad|name.txt", string.Empty, "C:\\Temp\\a.txt"],
            out var rejected);

        Assert.Single(result);
        Assert.Equal("C:\\Temp\\a.txt", result[0]);
        Assert.Equal(3, rejected); // 相对路径 + 非法字符 + 空串（重复项不计入 rejected）
    }

    [Fact]
    public void 超出条数上限被截断()
    {
        var input = Enumerable.Range(0, PathValidator.MaxPaths + 5)
            .Select(i => $"C:\\Temp\\file{i}.txt")
            .ToArray();

        var result = PathValidator.Filter(input, out var rejected);

        Assert.Equal(PathValidator.MaxPaths, result.Count);
        Assert.Equal(5, rejected);
    }

    [Fact]
    public void 超长路径被拒绝()
    {
        var longPath = "C:\\" + new string('a', PathValidator.MaxPathLength);

        var result = PathValidator.Filter([longPath], out var rejected);

        Assert.Empty(result);
        Assert.Equal(1, rejected);
    }

    [Fact]
    public void DROPFILES结构正确()
    {
        var bytes = DropFilesWriter.Build(["C:\\a.txt", "D:\\b.txt"]);

        Assert.Equal(DropFilesWriter.DropFilesHeaderSize, (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16))); // fWide

        var text = Encoding.Unicode.GetString(bytes, DropFilesWriter.DropFilesHeaderSize, bytes.Length - DropFilesWriter.DropFilesHeaderSize);
        Assert.Equal("C:\\a.txt\0D:\\b.txt\0\0", text);
    }

    [Fact]
    public void 空路径列表_抛异常() =>
        Assert.Throws<ArgumentException>(() => DropFilesWriter.Build([]));
}
