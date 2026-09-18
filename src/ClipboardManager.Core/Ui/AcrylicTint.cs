namespace ClipboardManager.Core.Ui;

/// <summary>
/// 亚克力（Acrylic）强度到「底色不透明度」的映射（附加项 B-04，纯函数便于单测）。
/// <para>
/// 约定：<b>强度 0 = 完全不透明（关闭亚克力）</b>，强度越大越透。
/// 即使强度拉满也保留 <see cref="MinOpacityPercent"/> 的底色 —— 否则文字会完全糊在壁纸上不可读。
/// </para>
/// <para>
/// 颜色打包遵循 <c>SetWindowCompositionAttribute</c> 的 ACCENT_POLICY.GradientColor 约定：
/// <b>0xAABBGGRR</b>（注意是 ABGR，不是常见的 ARGB）。
/// </para>
/// </summary>
public static class AcrylicTint
{
    /// <summary>强度上限（百分比）。</summary>
    public const int MaxStrength = 100;

    /// <summary>
    /// 强度上限时仍保留的最低保保不透明度（百分比）。
    /// <para>
    /// 2026-09-19 由 45% 下调到 30%：改用真正会模糊的 accent 3 之后，底色更透才能看出磨砂效果
    /// （用户反馈"模糊强度太低"）；文字可读性由用户在滑杆上自行权衡。
    /// </para>
    /// </summary>
    public const int MinOpacityPercent = 30;

    /// <summary>把任意输入收敛到 0–100。</summary>
    public static int Normalize(int strength) => Math.Clamp(strength, 0, MaxStrength);

    /// <summary>是否启用亚克力（强度 &gt; 0）。</summary>
    public static bool IsEnabled(int strength) => Normalize(strength) > 0;

    /// <summary>强度对应的底色不透明度百分比（0 强度 = 100% 不透明）。</summary>
    public static int OpacityPercent(int strength)
    {
        var normalized = Normalize(strength);
        var range = 100 - MinOpacityPercent;
        return 100 - (int)Math.Round(range * (normalized / 100.0));
    }

    /// <summary>强度对应的 alpha 字节（0–255）。</summary>
    public static int AlphaByte(int strength) => (int)Math.Round(255 * OpacityPercent(strength) / 100.0);

    /// <summary>
    /// 打包为 ACCENT_POLICY.GradientColor（0xAABBGGRR）。
    /// </summary>
    /// <param name="red">底色红分量。</param>
    /// <param name="green">底色绿分量。</param>
    /// <param name="blue">底色蓝分量。</param>
    /// <param name="strength">亚克力强度（0–100）。</param>
    public static uint PackAbgr(byte red, byte green, byte blue, int strength) =>
        ((uint)AlphaByte(strength) << 24) | ((uint)blue << 16) | ((uint)green << 8) | red;
}
