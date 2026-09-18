using System.Runtime.InteropServices;

namespace ClipboardManager.Interop;

/// <summary>
/// 剪贴板的唯一读写入口（阶段一：只处理 <c>CF_UNICODETEXT</c>）。
/// <para>
/// 三条铁律（需求 §5.2 / §5.3，AGENTS.md §3）：
/// ① 打开失败（ERROR_ACCESS_DENIED）要重试，但总耗时设上限，不能长时间阻塞；
/// ② 锁内只做「拷贝到托管内存」，编码 / 哈希 / 磁盘 / UI 一律在锁外；
/// ③ 调用方必须在 STA 线程上使用本类。
/// </para>
/// </summary>
public sealed class ClipboardAccess
{
    /// <summary>文本长度上限（字符）。512K 字符 ≈ 1MB（UTF-16），超出截断（需求 §4.3 输入上限）。</summary>
    public const int MaxTextChars = 512 * 1024;

    private const int MaxAttempts = 4;
    private const int RetryDelayMs = 40;

    private readonly IntPtr _ownerWindow;

    /// <summary>创建访问器。</summary>
    /// <param name="ownerWindow">打开剪贴板时登记的所有者窗口（仅消息窗口句柄）。</param>
    public ClipboardAccess(IntPtr ownerWindow) => _ownerWindow = ownerWindow;

    /// <summary>读取剪贴板文本。返回 false 且 <paramref name="error"/> 为 null 表示「当前没有文本内容」，属正常情况。</summary>
    /// <param name="text">读到的文本（已拷贝到托管内存）。</param>
    /// <param name="error">失败原因（含重试耗尽）。</param>
    public bool TryReadText(out string? text, out string? error)
    {
        text = null;
        error = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (!NativeMethods.OpenClipboard(_ownerWindow))
            {
                var code = Marshal.GetLastWin32Error();
                if (code == NativeMethods.ERROR_ACCESS_DENIED && attempt < MaxAttempts)
                {
                    Thread.Sleep(RetryDelayMs);
                    continue;
                }

                if (code == NativeMethods.ERROR_ACCESS_DENIED)
                {
                    error = $"剪贴板被其它程序占用（已重试 {MaxAttempts} 次）";
                    return false;
                }

                error = $"打开剪贴板失败，Win32 错误码 {code}";
                return false;
            }

            // 从这里到 CloseClipboard 之间只允许「拷贝」操作。
            try
            {
                if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
                {
                    return false; // 没有文本格式：正常情况（图片 / 文件等阶段二再处理）
                }

                var handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
                if (handle == IntPtr.Zero)
                {
                    error = $"读取剪贴板文本失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
                    return false;
                }

                var pointer = NativeMethods.GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                {
                    error = $"锁定剪贴板内存失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
                    return false;
                }

                try
                {
                    var byteSize = NativeMethods.GlobalSize(handle);
                    text = ReadUnicodeString(pointer, byteSize);
                    return true;
                }
                finally
                {
                    NativeMethods.GlobalUnlock(handle);
                }
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        error = "读取剪贴板失败";
        return false;
    }

    /// <summary>
    /// 写入剪贴板文本。
    /// </summary>
    /// <param name="value">要写入的文本（必须是原文，脱敏只用于显示）。</param>
    /// <param name="sequenceAfterWrite">写入完成后的剪贴板序列号，供自循环过滤使用。</param>
    /// <param name="error">失败原因。</param>
    public bool TryWriteText(string value, out long sequenceAfterWrite, out string? error)
    {
        ArgumentNullException.ThrowIfNull(value);
        sequenceAfterWrite = 0;
        error = null;

        var bytes = ((long)value.Length + 1) * 2;
        if (bytes > int.MaxValue)
        {
            error = "文本过长，拒绝写入剪贴板";
            return false;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var hMem = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)bytes);
            if (hMem == IntPtr.Zero)
            {
                error = $"分配剪贴板内存失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
                return false;
            }

            var pointer = NativeMethods.GlobalLock(hMem);
            if (pointer == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(hMem);
                error = $"锁定剪贴板内存失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
                return false;
            }

            try
            {
                var chars = value.ToCharArray();
                Marshal.Copy(chars, 0, pointer, chars.Length);
                Marshal.WriteInt16(pointer, chars.Length * 2, 0); // 结尾 NUL
            }
            finally
            {
                NativeMethods.GlobalUnlock(hMem);
            }

            if (!NativeMethods.OpenClipboard(_ownerWindow))
            {
                NativeMethods.GlobalFree(hMem); // 未交给系统，必须自行释放
                var code = Marshal.GetLastWin32Error();
                if (code == NativeMethods.ERROR_ACCESS_DENIED && attempt < MaxAttempts)
                {
                    Thread.Sleep(RetryDelayMs);
                    continue;
                }

                error = $"打开剪贴板失败，Win32 错误码 {code}";
                return false;
            }

            try
            {
                if (!NativeMethods.EmptyClipboard())
                {
                    NativeMethods.GlobalFree(hMem);
                    error = $"清空剪贴板失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
                    return false;
                }

                if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hMem) == IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(hMem); // 失败：所有权仍在自己
                    error = $"写入剪贴板失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
                    return false;
                }

                // 成功：内存所有权已移交系统，之后绝不能再释放 hMem。
                sequenceAfterWrite = NativeMethods.GetClipboardSequenceNumber();
                return true;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        error = "写入剪贴板失败";
        return false;
    }

    /// <summary>取当前剪贴板序列号（无需打开剪贴板）。</summary>
    public static long GetSequenceNumber() => NativeMethods.GetClipboardSequenceNumber();

    /// <summary>
    /// 取剪贴板来源进程名（可能失败，返回 null 属正常）。
    /// 用于「排除应用」的进程名辅助判定（D-06）。
    /// </summary>
    public static string? TryGetSourceProcessName()
    {
        var owner = NativeMethods.GetClipboardOwner();
        if (owner == IntPtr.Zero)
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(owner, out var processId);
        if (processId == 0)
        {
            return null;
        }

        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new char[512];
            var size = (uint)buffer.Length;
            return NativeMethods.QueryFullProcessImageNameW(process, 0, buffer, ref size)
                ? Path.GetFileName(new string(buffer, 0, (int)size))
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    /// <summary>
    /// 在长度上限内把非托管的 UTF-16 字符串拷到托管内存：
    /// 先按 <c>GlobalSize/2</c> 与 <see cref="MaxTextChars"/> 取最小值划定可读范围，
    /// 再在该范围内找 NUL 结尾。<b>绝不直接使用会无限读取的 API</b>（畸形剪贴板数据防越界）。
    /// </summary>
    private static unsafe string ReadUnicodeString(IntPtr pointer, nuint byteSize)
    {
        var maxChars = (int)Math.Min((ulong)byteSize / 2UL, (ulong)MaxTextChars);
        if (maxChars <= 0)
        {
            return string.Empty;
        }

        var span = new ReadOnlySpan<char>((void*)pointer, maxChars);
        var nul = span.IndexOf('\0');
        var length = nul >= 0 ? nul : span.Length;
        if (length > MaxTextChars)
        {
            length = MaxTextChars;
        }

        return new string(span[..length]);
    }
}
