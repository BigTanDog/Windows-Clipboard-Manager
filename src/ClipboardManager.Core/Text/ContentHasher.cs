using System.Security.Cryptography;
using System.Text;

namespace ClipboardManager.Core.Text;

/// <summary>
/// 内容哈希（去重键）。纯函数，无 IO。
/// </summary>
public static class ContentHasher
{
    /// <summary>对文本内容计算 SHA-256（UTF-8 编码），返回小写十六进制。</summary>
    public static string ForText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>对二进制内容计算 SHA-256，返回小写十六进制。</summary>
    public static string ForBinary(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>对文件路径列表计算 SHA-256（按顺序拼接，避免不同顺序被判为同一条）。</summary>
    public static string ForFileList(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var builder = new StringBuilder();
        foreach (var path in paths)
        {
            builder.Append(path).Append('\n');
        }

        return ForText(builder.ToString());
    }
}
