namespace ClipboardManager.App;

/// <summary>
/// 应用版本号（<b>单一来源</b>：<c>Directory.Build.props</c> 的 <c>Version</c> 属性 → 程序集版本）。
/// <para>
/// 版本约定（用户 2026-09-19 定）：<b>普通改动只 +1 最后一位</b>（1.1.0 → 1.1.1），
/// 加了较大功能再升中间位。展示形式为三段式（第四段通常恒为 0，不展示）。
/// </para>
/// </summary>
internal static class AppVersion
{
    /// <summary>用户可见的版本号，例如 <c>1.1.0</c>。</summary>
    public static string Display { get; } = Format(typeof(AppVersion).Assembly.GetName().Version);

    /// <summary>
    /// 把程序集版本格式化成三段式。
    /// </summary>
    /// <param name="version">程序集版本；<c>Build</c> 未定义时为 -1，这里按 0 处理。</param>
    public static string Format(Version? version) => version is null
        ? "未知"
        : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
}
