namespace ClipboardManager.Storage;

/// <summary>
/// 程序目录与数据目录解析（需求 §4.1 / §5.5 / §5.6）。
/// <para>
/// 单文件发布时 <c>Assembly.Location</c> 返回空字符串，因此这里以
/// <c>Environment.ProcessPath</c>（.NET 6+ 推荐方式）为主，<c>AppContext.BaseDirectory</c> 作回退，
/// 并把两者都记录下来供 <c>--diag</c> 对照（两条发布形态都要实测，见技术设计 §7.2）。
/// </para>
/// </summary>
public static class AppPaths
{
    /// <summary>进程可执行文件所在目录（数据目录的基准）。</summary>
    public static string ProgramDirectory { get; } = ResolveProgramDirectory();

    /// <summary>AppContext.BaseDirectory（仅用于诊断对照，不参与定位）。</summary>
    public static string BaseDirectory { get; } = AppContext.BaseDirectory;

    /// <summary>进程可执行文件路径。</summary>
    public static string? ProcessPath { get; } = Environment.ProcessPath;

    /// <summary>数据目录：程序目录下 <c>data/</c>（不写 %APPDATA%）。</summary>
    public static string DataDirectory { get; } = Path.Combine(ProgramDirectory, "data");

    /// <summary>SQLite 数据库路径。</summary>
    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "history.db");

    /// <summary>二进制本体目录（阶段二启用）。</summary>
    public static string BlobDirectory { get; } = Path.Combine(DataDirectory, "blobs");

    /// <summary>收藏本体目录（阶段三启用）。</summary>
    public static string PinnedDirectory { get; } = Path.Combine(DataDirectory, "pinned");

    /// <summary>日志目录。</summary>
    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    /// <summary>设置文件路径。</summary>
    public static string SettingsPath { get; } = Path.Combine(DataDirectory, "settings.json");

    /// <summary>
    /// 确保数据目录存在且可写。
    /// 不可写（例如程序被放在 Program Files 下）时返回 false 并给出明确原因，
    /// <b>不静默降级到 %APPDATA%</b>（需求 §5.5）。
    /// </summary>
    /// <param name="error">失败原因。</param>
    public static bool TryEnsureWritable(out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var probe = Path.Combine(DataDirectory, ".write-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            error = $"数据目录不可写：{DataDirectory}（{ex.GetType().Name}: {ex.Message}）";
            return false;
        }
    }

    private static string ResolveProgramDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            var directory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrEmpty(directory))
            {
                return directory;
            }
        }

        return AppContext.BaseDirectory;
    }
}
