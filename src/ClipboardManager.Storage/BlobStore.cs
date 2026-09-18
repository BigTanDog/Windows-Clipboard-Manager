namespace ClipboardManager.Storage;

/// <summary>
/// 二进制本体存储（图片 PNG、HTML 片段），按内容哈希命名（天然去重），
/// 目录结构 <c>data/blobs/&lt;hash 前两位&gt;/&lt;hash&gt;.&lt;ext&gt;</c>（技术设计 §7.2）。
/// <para>
/// 安全要点（AGENTS.md §6「路径处理」）：所有对外暴露的路径都是<b>相对 data 目录</b>的，
/// 读取/删除前必须做「规范化 + 前缀校验」，杜绝 <c>../</c> 之类的越权访问。
/// </para>
/// </summary>
public sealed class BlobStore
{
    private const string BlobDirectoryName = "blobs";

    /// <summary>允许的本体扩展名白名单。</summary>
    private static readonly string[] AllowedExtensions = ["png", "html"];

    private readonly string _dataDirectory;
    private readonly string _blobRoot;
    private readonly AppLog? _log;

    /// <summary>创建存储。</summary>
    /// <param name="dataDirectory">data 目录绝对路径。</param>
    /// <param name="log">日志器。</param>
    public BlobStore(string dataDirectory, AppLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _blobRoot = Path.Combine(_dataDirectory, BlobDirectoryName);
        _log = log;
    }

    /// <summary>blobs 根目录（供诊断）。</summary>
    public string Root => _blobRoot;

    /// <summary>
    /// 保存本体（幂等：同哈希同扩展名直接复用已有文件），返回相对 data 目录的路径。
    /// </summary>
    /// <param name="data">内容字节。</param>
    /// <param name="contentHash">内容哈希（小写十六进制）。</param>
    /// <param name="extension">扩展名（不含点，必须在白名单内）。</param>
    /// <param name="thumbnail">是否为缩略图（文件名加 <c>.thumb</c> 后缀）。</param>
    public string Save(byte[] data, string contentHash, string extension, bool thumbnail = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        var normalizedHash = NormalizeHash(contentHash);
        var normalizedExtension = NormalizeExtension(extension);

        var relativePath = BuildRelativePath(normalizedHash, normalizedExtension, thumbnail);
        var fullPath = ResolveSafe(relativePath);

        if (File.Exists(fullPath))
        {
            return relativePath; // 内容寻址：已存在即相同内容，直接复用
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // 原子写：先写临时文件再替换，避免中断留下半个文件。
        var temporary = fullPath + ".tmp";
        File.WriteAllBytes(temporary, data);
        File.Move(temporary, fullPath, overwrite: true);
        return relativePath;
    }

    /// <summary>读取本体；文件不存在或路径非法时返回 null（越权路径不会抛给调用方）。</summary>
    /// <param name="relativePath">相对 data 目录的路径。</param>
    public byte[]? TryRead(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return null;
        }

        try
        {
            var fullPath = ResolveSafe(relativePath);
            return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log?.Error("读取本体文件失败", ex);
            return null;
        }
    }

    /// <summary>删除本体（幂等）。</summary>
    /// <param name="relativePath">相对 data 目录的路径。</param>
    public bool TryDelete(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        try
        {
            var fullPath = ResolveSafe(relativePath);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            File.Delete(fullPath);
            return true;
        }
        catch (Exception ex)
        {
            _log?.Error("删除本体文件失败", ex);
            return false;
        }
    }

    /// <summary>由本体路径推导缩略图路径（同一目录，文件名加 <c>.thumb</c>）。</summary>
    public static string? ThumbnailPathFor(string? blobPath)
    {
        if (string.IsNullOrEmpty(blobPath))
        {
            return null;
        }

        var extension = Path.GetExtension(blobPath);
        return extension.Length == 0
            ? blobPath + ".thumb"
            : blobPath[..^extension.Length] + ".thumb" + extension;
    }

    /// <summary>
    /// 清理孤儿文件：扫描 blobs 目录，删除「不被任何记录引用」的文件（含缩略图）。
    /// 启动时调用（此时没有在途写入，安全）。
    /// </summary>
    /// <param name="referencedPaths">数据库中仍被引用的本体路径集合。</param>
    /// <returns>删除的文件数。</returns>
    public int CollectOrphans(IReadOnlyCollection<string> referencedPaths)
    {
        ArgumentNullException.ThrowIfNull(referencedPaths);

        if (!Directory.Exists(_blobRoot))
        {
            return 0;
        }

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in referencedPaths)
        {
            if (!string.IsNullOrEmpty(path))
            {
                referenced.Add(Path.GetFullPath(Path.Combine(_dataDirectory, path)));
                var thumb = ThumbnailPathFor(path);
                if (!string.IsNullOrEmpty(thumb))
                {
                    referenced.Add(Path.GetFullPath(Path.Combine(_dataDirectory, thumb)));
                }
            }
        }

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(_blobRoot, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(file, ref removed);
                continue;
            }

            if (!referenced.Contains(Path.GetFullPath(file)))
            {
                TryDeleteFile(file, ref removed);
            }
        }

        if (removed > 0)
        {
            _log?.Info($"清理孤儿本体文件 {removed} 个");
        }

        return removed;
    }

    private void TryDeleteFile(string fullPath, ref int removed)
    {
        try
        {
            File.Delete(fullPath);
            removed++;
        }
        catch (Exception ex)
        {
            _log?.Error("清理孤儿文件失败", ex);
        }
    }

    private string BuildRelativePath(string hash, string extension, bool thumbnail)
    {
        var fileName = thumbnail ? $"{hash}.thumb.{extension}" : $"{hash}.{extension}";
        // 用哈希前两位分桶，避免单目录文件过多
        return Path.Combine(BlobDirectoryName, hash[..2], fileName);
    }

    /// <summary>把相对路径解析为绝对路径并做前缀校验（防目录穿越）。</summary>
    private string ResolveSafe(string relativePath)
    {
        var combined = Path.GetFullPath(Path.Combine(_dataDirectory, relativePath));
        var rootWithSeparator = _blobRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _blobRoot
            : _blobRoot + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"本体路径越权：{relativePath}");
        }

        return combined;
    }

    private static string NormalizeHash(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        if (hash.Length < 4)
        {
            throw new ArgumentException("内容哈希过短，无法分桶", nameof(hash));
        }

        foreach (var ch in hash)
        {
            if (!char.IsAsciiHexDigitLower(ch))
            {
                throw new ArgumentException("内容哈希必须是十六进制字符串", nameof(hash));
            }
        }

        return hash;
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var normalized = extension.TrimStart('.').ToLowerInvariant();
        if (!AllowedExtensions.Contains(normalized))
        {
            throw new ArgumentException($"不支持的本体扩展名 {extension}", nameof(extension));
        }

        return normalized;
    }
}
