using ClipboardManager.Core.Clipboard;

namespace ClipboardManager.Tests;

/// <summary>
/// 自循环过滤测试（需求 §5.1 / 验收标准 6）：粘贴写回后不得产生新记录。
/// </summary>
public class SelfWriteFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 序列号命中即忽略()
    {
        var filter = new SelfWriteFilter();
        filter.NoteOwnWrite(42, "hash-a", Now);
        Assert.True(filter.ShouldIgnore(42, "hash-a", Now));
    }

    [Fact]
    public void 序列号不同但窗口内同哈希也忽略()
    {
        var filter = new SelfWriteFilter();
        filter.NoteOwnWrite(42, "hash-a", Now);

        // 例：其它程序在我们写入后又刷新了剪贴板，序列号变了但内容仍是我们的。
        Assert.True(filter.ShouldIgnore(43, "hash-a", Now.AddMilliseconds(500)));
    }

    [Fact]
    public void 超出时间窗不再忽略()
    {
        var filter = new SelfWriteFilter();
        filter.NoteOwnWrite(42, "hash-a", Now);
        Assert.False(filter.ShouldIgnore(43, "hash-a", Now + SelfWriteFilter.HashWindow + TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void 不同内容不忽略()
    {
        var filter = new SelfWriteFilter();
        filter.NoteOwnWrite(42, "hash-a", Now);
        Assert.False(filter.ShouldIgnore(43, "hash-b", Now));
    }

    [Fact]
    public void 未记录写入时不忽略()
    {
        var filter = new SelfWriteFilter();
        Assert.False(filter.ShouldIgnore(1, "whatever", Now));
    }

    [Fact]
    public void 重置后不再忽略()
    {
        var filter = new SelfWriteFilter();
        filter.NoteOwnWrite(42, "hash-a", Now);
        filter.Reset();
        Assert.False(filter.ShouldIgnore(42, "hash-a", Now));
    }
}
