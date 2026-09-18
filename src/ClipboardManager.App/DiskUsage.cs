using System.Diagnostics;
using System.IO;
using ClipboardManager.Core.Text;
using ClipboardManager.Storage;

namespace ClipboardManager.App;

/// <summary>历史数据的磁盘占用快照（设置页展示用）。</summary>
/// <param name="TotalBytes">数据库 + 图片/HTML 本体合计字节数。</param>
/// <param name="DatabaseBytes">数据库文件字节数。</param>
/// <param name="BlobBytes">图片/HTML 本体合计字节数。</param>
/// <param name="FileCount">本体文件个数。</param>
/// <param name="RecordCount">记录条数。</param>
/// <remarks>设为 public 是因为它出现在 <see cref="SettingsWindow"/> 的公开构造函数签名里。</remarks>
public sealed record DiskUsageInfo(long TotalBytes, long DatabaseBytes, long BlobBytes, int FileCount, int RecordCount)
{
    /// <summary>「已用 / 上限」文案（上限为负表示不限）。</summary>
    public string Format(int limitMegabytes) => ByteSize.FormatWithLimit(TotalBytes, limitMegabytes);

    /// <summary>明细文案：数据库 / 本体 / 条数。</summary>
    public string Describe()
    {
        var detail = $"数据库 {ByteSize.Format(DatabaseBytes)} · 缓存文件 {FileCount} 个（{ByteSize.Format(BlobBytes)}）";
        return RecordCount > 0 ? $"{detail} · 记录 {RecordCount} 条" : detail;
    }
}

/// <summary>
/// 统计与打开数据目录。
/// <para>
/// 口径说明：只统计「历史数据」（<c>history.db</c> 与 <c>blobs/</c>），
/// 不含 <c>logs/</c> 与设置文件 —— 这样显示的占用与「清空历史后应该释放多少」一致。
/// </para>
/// </summary>
internal static class DiskUsage
{
    /// <summary>统计当前占用（目录不存在时按 0 计）。</summary>
    /// <param name="recordCount">记录条数（由仓储提供）。</param>
    public static DiskUsageInfo Measure(int recordCount)
    {
        var databaseBytes = TryFileSize(AppPaths.DatabasePath);

        long blobBytes = 0;
        var fileCount = 0;
        MeasureDirectory(AppPaths.BlobDirectory, ref blobBytes, ref fileCount);

        return new DiskUsageInfo(databaseBytes + blobBytes, databaseBytes, blobBytes, fileCount, recordCount);
    }

    /// <summary>在资源管理器里打开缓存目录（不存在则先创建）。</summary>
    /// <param name="path">要打开的目录。</param>
    /// <returns>是否已发起打开。</returns>
    public static bool OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            // 打不开文件夹不影响其它功能，静默失败（调用方只用于按钮反馈）。
            return false;
        }
    }

    private static long TryFileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void MeasureDirectory(string directory, ref long totalBytes, ref int fileCount)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                totalBytes += file.Length;
                fileCount++;
            }
        }
        catch (IOException)
        {
            // 目录正在被其它进程操作（如资源管理器预览）：本次统计忽略，下次刷新会重新读。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
