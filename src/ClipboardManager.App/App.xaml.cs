using System.Globalization;
using System.Windows;

namespace ClipboardManager.App;

/// <summary>
/// 应用入口。参数：
/// <list type="bullet">
/// <item><c>--diag</c>：输出诊断日志（启动耗时、目录定位、自循环判定等）。</item>
/// <item><c>--duration=N</c>：N 秒后自动退出（仅用于体积 / 内存 / 冷启动的自动化测量）。</item>
/// </list>
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private AppHost? _host;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var (diag, durationSeconds) = ParseArguments(e.Args);

        // 单实例守卫：两个实例会各自注册监听与热键（热键第二次必然失败），
        // 且会重复记录剪贴板内容，因此这里直接拦住第二个实例。
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\ClipboardManager.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "剪贴板管理器已经在运行。\n如需重启，请先在任务管理器中结束 ClipboardManager 进程。",
                "剪贴板管理器",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(1);
            return;
        }

        try
        {
            _host = new AppHost(diag, durationSeconds);
            _host.Start();
        }
        catch (Exception ex)
        {
            // 先落日志再弹窗：日志是自动化验证与发布版本定位故障的唯一凭据。
            var log = new Storage.AppLog(Storage.AppPaths.LogDirectory, verbose: true);
            log.Error($"启动失败（程序目录 {Storage.AppPaths.ProgramDirectory}）", ex);

            if (!diag)
            {
                MessageBox.Show(
                    $"启动失败：{ex.Message}\n\n详情见 data\\logs\\app.log\n程序目录：{Storage.AppPaths.ProgramDirectory}",
                    "剪贴板管理器",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            Shutdown(2);
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static (bool Diag, int DurationSeconds) ParseArguments(string[] args)
    {
        var diag = false;
        var duration = 0;

        foreach (var argument in args)
        {
            if (string.Equals(argument, "--diag", StringComparison.OrdinalIgnoreCase))
            {
                diag = true;
                continue;
            }

            const string durationPrefix = "--duration=";
            if (argument.StartsWith(durationPrefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(
                    argument.AsSpan(durationPrefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var seconds)
                && seconds > 0)
            {
                duration = seconds;
            }
        }

        return (diag, duration);
    }
}
