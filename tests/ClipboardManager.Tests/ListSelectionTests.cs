using ClipboardManager.Core.Ui;

namespace ClipboardManager.Tests;

/// <summary>
/// 列表刷新后的选中项决策（用户 2026-09-30 反馈：删除后选中"下一条"而不是跳回顶部）。
/// </summary>
public class ListSelectionTests
{
    [Fact]
    public void 选中的条目还在时保持同一条不跳()
    {
        // 列表前面插入了新条目（索引后移），仍应盯着同一条记录
        Assert.Equal(2, ListSelection.Resolve([10, 11, 12, 13], previousId: 12, previousIndex: 1, keepSelection: true));
    }

    [Fact]
    public void 选中的条目被删除后落在顶上来的下一条()
    {
        // 删除前列表 [10, 11, 12, 13] 选中的是 id=12（下标 2）；删除后 12 消失，
        // 应选新的下标 2 —— 也就是原来的下一条 id=13 顶上来的位置。
        long[] afterDelete = [10, 11, 13];
        var index = ListSelection.Resolve(afterDelete, previousId: 12, previousIndex: 2, keepSelection: true);

        Assert.Equal(2, index);
        Assert.Equal(13, afterDelete[index]);
    }

    [Fact]
    public void 删除最后一条时落到新的最后一条()
    {
        Assert.Equal(1, ListSelection.Resolve([10, 11], previousId: 12, previousIndex: 2, keepSelection: true));
    }

    [Fact]
    public void 原位置越界时收敛到可用的最后一条()
    {
        // 搜索过滤后只剩一条，而原本选中的位置远在末尾
        Assert.Equal(0, ListSelection.Resolve([10], previousId: 12, previousIndex: 5, keepSelection: true));
    }

    [Fact]
    public void 面板重新弹出时回到第一条()
    {
        Assert.Equal(0, ListSelection.Resolve([10, 11], previousId: 11, previousIndex: 1, keepSelection: false));
    }

    [Fact]
    public void 原本没有选中时选第一条()
    {
        Assert.Equal(0, ListSelection.Resolve([10, 11], previousId: null, previousIndex: -1, keepSelection: true));
    }

    [Fact]
    public void 列表为空时没有可选项()
    {
        Assert.Equal(ListSelection.None, ListSelection.Resolve([], previousId: 11, previousIndex: 1, keepSelection: true));
        Assert.Equal(ListSelection.None, ListSelection.Resolve([], previousId: null, previousIndex: -1, keepSelection: false));
    }
}
