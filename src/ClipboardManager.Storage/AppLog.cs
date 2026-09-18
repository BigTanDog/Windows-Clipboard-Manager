using System.Text;

namespace ClipboardManager.Storage;

/// <summary>
/// 极简滚动日志（AGENTS.md §4：日志不得输出完整剪贴板文本，最多 50 字符）。
/// <para>
/// 约束：单文件上限 2MB，保留 3 个（app.log / app.1.log / app.2.log）。
/// 写日志失败绝不影响主流程（磁盘满、文件被占用都只静默忽略）。
/// </para>
/// </summary>
public sealed class AppLog
{
    /// <summary>日志中允许出现的剪贴板内容最大长度（AGENTS.md §4）。</summary>
    public const int MaxContentChars = 50;

    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxFiles = 3;

    private readonly object _gate = new();
    private readonly string? _logPath;

    /// <summary>创建日志器。</summary>
    /// <param name="logDirectory">日志目录（null 或不可用时退化为不写文件）。</param>
    /// <param name="verbose">是否输出诊断级信息（--diag）。</param>
    public AppLog(string? logDirectory, bool verbose = false)
    {
        Verbose = verbose;
        if (string.IsNullOrEmpty(logDirectory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(logDirectory);
            _logPath = Path.Combine(logDirectory, "app.log");
        }
        catch
        {
            _logPath = null;
        }
    }

    /// <summary>是否输出诊断细节。</summary>
    public bool Verbose { get; }

    /// <summary>普通信息。</summary>
    public void Info(string message) => Write("INFO ", message, null);

    /// <summary>诊断细节（仅在 <see cref="Verbose"/> 为 true 时写入）。</summary>
    public void Diag(string message)
    {
        if (Verbose)
        {
            Write("DIAG ", message, null);
        }
    }

    /// <summary>
    /// 错误。会记录异常的类型、消息与堆栈（堆栈只含方法名与路径，不含剪贴板内容），
    /// 以便发布版本（DebugType=none，无可读行号）仍能定位问题。
    /// </summary>
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    /// <summary>
    /// 把可能来自剪贴板的内容裁到 <see cref="MaxContentChars"/> 字符以内再进日志。
    /// 调用方负责在裁剪后再做脱敏（技术设计 §6.5 应用点 4）。
    /// </summary>
    public static string TruncateContent(string? content) =>
        content is null ? string.Empty
        : content.Length <= MaxContentChars ? content
        : string.Concat(content.AsSpan(0, MaxContentChars), "…");

    private void Write(string level, string message, Exception? exception)
    {
        if (_logPath is null)
        {
            return;
        }

        var builder = new StringBuilder(message.Length + 64);
        builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ')
            .Append(level).Append(' ').Append(message);
        if (exception is not null)
        {
            builder.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);

            // 堆栈对定位启动期故障是必需的；截断上限防止异常链过长挤占日志。
            var stack = exception.ToString();
            const int maxStackChars = 2000;
            builder.Append(" | ").Append(
                stack.Length <= maxStackChars ? stack : string.Concat(stack.AsSpan(0, maxStackChars), "…"));
        }

        var line = builder.ToString();

        lock (_gate)
        {
            try
            {
                RollIfNeeded();
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 日志失败不影响主流程。
            }
        }
    }

    private void RollIfNeeded()
    {
        if (_logPath is null || !File.Exists(_logPath))
        {
            return;
        }

        if (new FileInfo(_logPath).Length < MaxFileBytes)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_logPath)!;
        var baseName = Path.GetFileNameWithoutExtension(_logPath);
        var extension = Path.GetExtension(_logPath);

        var oldest = Path.Combine(directory, $"{baseName}.{MaxFiles - 1}{extension}");
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = MaxFiles - 2; index >= 1; index--)
        {
            var source = Path.Combine(directory, $"{baseName}.{index}{extension}");
            if (File.Exists(source))
            {
                File.Move(source, Path.Combine(directory, $"{baseName}.{index + 1}{extension}"), overwrite: true);
            }
        }

        File.Move(_logPath, Path.Combine(directory, $"{baseName}.1{extension}"), overwrite: true);
    }
}
