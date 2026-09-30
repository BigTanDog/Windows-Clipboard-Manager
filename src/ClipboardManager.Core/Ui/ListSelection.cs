namespace ClipboardManager.Core.Ui;

/// <summary>
/// 列表刷新后「该选中哪一条」的决策（纯函数，可单测）。
/// <para>
/// 背景（用户 2026-09-30 反馈）：面板每次刷新列表都一律把选中项重置为第一条，于是删掉一条后
/// 选中会"跳回顶部"—— 连按 Delete 时很容易顺手删掉别的东西。规则改为：
/// </para>
/// <list type="number">
/// <item>刷新前选中的那条还在 → 继续选中它（列表内容变了也不跳）；</item>
/// <item>那条已经没了（刚被删除）→ 选中它<b>原来的位置</b>，也就是删除后顶上来的那一条（即下一条）；</item>
/// <item>原位置已越界（删的是最后一条）→ 选中新的最后一条；</item>
/// <item>面板重新弹出（<paramref name="keepSelection"/> = false）→ 回到第一条（保持既有交互不变）。</item>
/// </list>
/// </summary>
public static class ListSelection
{
    /// <summary>没有可选项时的下标（WPF 列表用它表示"无选中"）。</summary>
    public const int None = -1;

    /// <summary>计算刷新后应选中的下标；返回 <see cref="None"/> 表示列表已空。</summary>
    /// <param name="itemIds">刷新后的记录主键（与列表顺序一致）。</param>
    /// <param name="previousId">刷新前选中的记录主键（无选中为 null）。</param>
    /// <param name="previousIndex">刷新前选中的下标（无选中为负）。</param>
    /// <param name="keepSelection">false = 强制回到第一条（面板每次弹出时使用）。</param>
    public static int Resolve(
        IReadOnlyList<long> itemIds,
        long? previousId,
        int previousIndex,
        bool keepSelection)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Count == 0)
        {
            return None;
        }

        if (!keepSelection)
        {
            return 0;
        }

        if (previousId is { } id)
        {
            for (var index = 0; index < itemIds.Count; index++)
            {
                if (itemIds[index] == id)
                {
                    return index;
                }
            }
        }

        // 选中的那条已被删除：落在它原来的位置（顶上来的下一条），越界则取新的最后一条。
        return previousIndex < 0 ? 0 : Math.Min(previousIndex, itemIds.Count - 1);
    }
}
