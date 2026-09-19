using System.Runtime.InteropServices;
using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Html;
using ClipboardManager.Core.Models;

namespace ClipboardManager.Interop;

/// <summary>
/// 剪贴板读写的唯一入口（阶段二起支持文本 / 图片 / 富文本 / 文件列表四种格式）。
/// <para>
/// 三条铁律（需求 §5.2 / §5.3，AGENTS.md §3）：
/// ① 打开失败（ERROR_ACCESS_DENIED）要重试，但总耗时设上限，不能长时间阻塞；
/// ② 锁内只做「拷贝到托管内存」，编码 / 哈希 / 磁盘 / UI 一律在锁外；
/// ③ 调用方必须在 STA 线程上使用本类。
/// </para>
/// </summary>
public sealed class ClipboardAccess
{
    /// <summary>文本长度上限（字符）。512K 字符 ≈ 1MB（UTF-16），超出截断。</summary>
    public const int MaxTextChars = 512 * 1024;

    /// <summary>二进制格式（图片 / HTML）字节上限 32MB，超过直接拒绝记录。</summary>
    public const int MaxBinaryBytes = 32 * 1024 * 1024;

    /// <summary>
    /// 打开剪贴板的重试预算：6 次 × 50ms（最坏约 300ms）。
    /// <para>
    /// 为什么给到 300ms：这条通知只来一次（同一序列号不会重放），读失败就等于永久丢一条记录，
    /// 而剪贴板锁通常只被占用几十毫秒 —— 多等一会儿远好过丢数据。
    /// 锁内不做慢操作的原则不受影响（等待发生在 OpenClipboard 之外）。
    /// </para>
    /// </summary>
    private const int MaxAttempts = 6;

    private const int RetryDelayMs = 50;

    private static readonly Lazy<uint> HtmlFormatId =
        new(() => NativeMethods.RegisterClipboardFormatW("HTML Format"));

    private readonly IntPtr _ownerWindow;

    /// <summary>创建访问器。</summary>
    /// <param name="ownerWindow">打开剪贴板时登记的所有者窗口（仅消息窗口句柄）。</param>
    public ClipboardAccess(IntPtr ownerWindow) => _ownerWindow = ownerWindow;

    /// <summary><c>"HTML Format"</c> 的注册格式 ID（进程内惰性注册并缓存）。</summary>
    public static uint HtmlFormat => HtmlFormatId.Value;

    /// <summary>
    /// 按需求 §3.2 的优先级读取剪贴板：文件路径 → 图片 → 富文本 → 纯文本，
    /// 一次开锁内完成探测与拷贝。返回 false 且 <paramref name="error"/> 为 null 表示
    /// 「当前没有受支持的内容」，属正常情况。
    /// </summary>
    public bool TryReadPayload(out ClipCandidate? payload, out string? error) =>
        TryReadPayloadWithSequence(out payload, out _, out error);

    /// <summary>
    /// 与 <see cref="TryReadPayload(out ClipCandidate?, out string?)"/> 相同，但额外返回
    /// <b>锁内</b>读到的剪贴板序列号 —— 它标记的正是这份内容，是「删除即吊销」判定的基础
    /// （配合 <see cref="TryClearIfSequence"/> 才能保证判定与清空之间不被插队）。
    /// </summary>
    /// <param name="payload">读到的候选；false 且 error 为 null 时表示没有受支持的格式。</param>
    /// <param name="sequence">锁内序列号（仅成功时有意义）。</param>
    /// <param name="error">失败原因。</param>
    public bool TryReadPayloadWithSequence(out ClipCandidate? payload, out long sequence, out string? error)
    {
        payload = null;
        sequence = 0;
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

                error = code == NativeMethods.ERROR_ACCESS_DENIED
                    ? $"剪贴板被其它程序占用（已重试 {MaxAttempts} 次）"
                    : $"打开剪贴板失败，Win32 错误码 {code}";
                return false;
            }

            // 锁内只允许拷贝到托管内存。
            try
            {
                var capturedAt = DateTimeOffset.Now;
                ClipCandidate? candidate = null;

                if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP))
                {
                    var paths = ReadFilePathsInLock();
                    if (paths.Count > 0)
                    {
                        candidate = new ClipCandidate
                        {
                            Type = ClipContentType.FileList,
                            FilePaths = paths,
                            CapturedAt = capturedAt,
                        };
                    }
                }

                if (candidate is null)
                {
                    var dibFormat = NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIBV5)
                        ? NativeMethods.CF_DIBV5
                        : NativeMethods.CF_DIB;
                    if (NativeMethods.IsClipboardFormatAvailable(dibFormat))
                    {
                        var dib = ReadBinaryInLock(dibFormat);
                        if (dib is not null)
                        {
                            candidate = new ClipCandidate
                            {
                                Type = ClipContentType.Image,
                                Binary = dib,
                                BlobExtension = "png",
                                CapturedAt = capturedAt,
                            };
                        }
                    }
                }

                if (candidate is null && NativeMethods.IsClipboardFormatAvailable(HtmlFormat))
                {
                    var html = ReadBinaryInLock(HtmlFormat);
                    if (html is not null)
                    {
                        candidate = new ClipCandidate
                        {
                            Type = ClipContentType.Html,
                            Binary = html,
                            BlobExtension = "html",
                            CapturedAt = capturedAt,
                        };
                    }
                }

                if (candidate is null && NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
                {
                    var text = ReadUnicodeTextInLock();
                    if (text is not null)
                    {
                        candidate = new ClipCandidate
                        {
                            Type = ClipContentType.Text,
                            Text = text,
                            CapturedAt = capturedAt,
                        };
                    }
                }

                if (candidate is null)
                {
                    return false; // 没有受支持的格式
                }

                sequence = NativeMethods.GetClipboardSequenceNumber();
                payload = candidate;
                return true;
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
    /// 条件清空剪贴板：<b>仅当序列号仍是 <paramref name="expectedSequence"/> 时</b>才清空。
    /// <para>
    /// 为什么必须带条件：调用方在此之前判定过「剪贴板里就是我们要吊销的那条记录」，
    /// 但判定与实际清空之间存在时间差，期间可能有别的程序写下新内容；
    /// 无条件清空就会毁掉用户刚复制的东西。复核发生在<b>持有剪贴板锁期间</b>，
    /// 因此「复核 + 清空」是原子的 —— 拿到锁以后没有任何程序能再改动剪贴板。
    /// </para>
    /// </summary>
    /// <param name="expectedSequence">判定时（或锁内读取时）取得的序列号。</param>
    public ClipboardClearResult TryClearIfSequence(long expectedSequence)
    {
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

                return new ClipboardClearResult(
                    ClipboardClearStatus.Failed,
                    0,
                    code == NativeMethods.ERROR_ACCESS_DENIED
                        ? $"剪贴板被其它程序占用（已重试 {MaxAttempts} 次），未清空"
                        : $"打开剪贴板失败，Win32 错误码 {code}");
            }

            try
            {
                if (NativeMethods.GetClipboardSequenceNumber() != expectedSequence)
                {
                    // 内容已被换掉：这次吊销作废，但也不该清 —— 保留用户的新内容。
                    return new ClipboardClearResult(ClipboardClearStatus.SequenceChanged, 0, null);
                }

                if (!NativeMethods.EmptyClipboard())
                {
                    return new ClipboardClearResult(
                        ClipboardClearStatus.Failed,
                        0,
                        $"清空剪贴板失败，Win32 错误码 {Marshal.GetLastWin32Error()}");
                }

                return new ClipboardClearResult(
                    ClipboardClearStatus.Cleared,
                    NativeMethods.GetClipboardSequenceNumber(),
                    null);
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        return new ClipboardClearResult(ClipboardClearStatus.Failed, 0, "清空剪贴板失败（已重试）");
    }

    /// <summary>
    /// 写回剪贴板（可同时写多种格式，D-08 要求 HTML 记录附带纯文本降级）。
    /// </summary>
    /// <param name="request">写入请求。</param>
    /// <param name="sequenceAfterWrite">写入后的剪贴板序列号，供自循环过滤使用。</param>
    /// <param name="error">失败原因。</param>
    public bool TryWrite(ClipboardWriteRequest request, out long sequenceAfterWrite, out string? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        sequenceAfterWrite = 0;
        error = null;

        if (!request.HasAnyFormat)
        {
            error = "写入请求不包含任何格式";
            return false;
        }

        // 第一步（锁外）：把所有格式的内存块都准备好。编码与拷贝都不占用全局剪贴板锁。
        var blocks = new List<(uint Format, IntPtr Handle)>(4);
        try
        {
            if (!string.IsNullOrEmpty(request.Text))
            {
                blocks.Add((NativeMethods.CF_UNICODETEXT, AllocateUnicodeText(request.Text)));
            }

            if (!string.IsNullOrEmpty(request.Html))
            {
                blocks.Add((HtmlFormat, Allocate(HtmlClipboardWriter.Build(request.Html))));
            }

            if (request.DibV5 is { Length: > 0 } dib)
            {
                blocks.Add((NativeMethods.CF_DIBV5, Allocate(dib)));
            }

            if (request.FilePaths is { Count: > 0 } paths)
            {
                blocks.Add((NativeMethods.CF_HDROP, Allocate(DropFilesWriter.Build(paths))));
            }
        }
        catch (Exception ex)
        {
            FreeAll(blocks);
            error = $"准备剪贴板数据失败：{ex.Message}";
            return false;
        }

        // 第二步：开锁后只是把已备好的内存交给系统，锁内不做任何耗时操作。
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

                FreeAll(blocks);
                error = $"打开剪贴板失败，Win32 错误码 {code}";
                return false;
            }

            var transferred = new List<IntPtr>(blocks.Count);
            try
            {
                if (!NativeMethods.EmptyClipboard())
                {
                    error = $"清空剪贴板失败，Win32 错误码 {Marshal.GetLastWin32Error()}";
                    return false;
                }

                foreach (var (format, handle) in blocks)
                {
                    if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
                    {
                        error = $"写入剪贴板失败（格式 {format}），Win32 错误码 {Marshal.GetLastWin32Error()}";
                        return false;
                    }

                    // 成功：内存所有权移交系统，之后绝不能再释放。
                    transferred.Add(handle);
                }

                sequenceAfterWrite = NativeMethods.GetClipboardSequenceNumber();
                return true;
            }
            finally
            {
                foreach (var (_, handle) in blocks)
                {
                    if (!transferred.Contains(handle))
                    {
                        NativeMethods.GlobalFree(handle);
                    }
                }

                NativeMethods.CloseClipboard();
            }
        }

        FreeAll(blocks);
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
    /// 锁内读取文本：先按 <c>GlobalSize/2</c> 与 <see cref="MaxTextChars"/> 取最小值划定可读范围，
    /// 再在该范围内找 NUL 结尾，<b>绝不调用会无限读取的 API</b>。
    /// </summary>
    private static unsafe string? ReadUnicodeTextInLock()
    {
        var handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var byteSize = NativeMethods.GlobalSize(handle);
            var maxChars = (int)Math.Min((ulong)byteSize / 2UL, (ulong)MaxTextChars);
            if (maxChars <= 0)
            {
                return string.Empty;
            }

            var span = new ReadOnlySpan<char>((void*)pointer, maxChars);
            var nul = span.IndexOf('\0');
            var length = nul >= 0 ? nul : span.Length;
            return new string(span[..Math.Min(length, MaxTextChars)]);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    /// <summary>锁内读取任意二进制格式：按 <see cref="MaxBinaryBytes"/> 设上限，超出返回 null。</summary>
    private static byte[]? ReadBinaryInLock(uint format)
    {
        var handle = NativeMethods.GetClipboardData(format);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var size = NativeMethods.GlobalSize(handle);
        if (size == 0 || size > (nuint)MaxBinaryBytes)
        {
            return null;
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new byte[(int)size];
            Marshal.Copy(pointer, buffer, 0, buffer.Length);
            return buffer;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    /// <summary>锁内读取 CF_HDROP 路径列表（原样返回，校验交给 <c>PathValidator</c>）。</summary>
    private static unsafe List<string> ReadFilePathsInLock()
    {
        var result = new List<string>();
        var drop = NativeMethods.GetClipboardData(NativeMethods.CF_HDROP);
        if (drop == IntPtr.Zero)
        {
            return result;
        }

        var count = NativeMethods.DragQueryFileW(drop, NativeMethods.DragQueryFileCount, null, 0);
        if (count == 0 || count > PathValidator.MaxPaths * 4)
        {
            // 条数异常（含畸形数据）直接放弃，避免为恶意数据分配大量内存。
            return result;
        }

        for (uint index = 0; index < count && result.Count < PathValidator.MaxPaths * 4; index++)
        {
            var length = NativeMethods.DragQueryFileW(drop, index, null, 0);
            if (length == 0 || length > PathValidator.MaxPathLength)
            {
                continue;
            }

            var buffer = new char[length + 1];
            var written = NativeMethods.DragQueryFileW(drop, index, buffer, (uint)buffer.Length);
            if (written > 0)
            {
                result.Add(new string(buffer, 0, (int)written));
            }
        }

        return result;
    }

    /// <summary>分配 HGLOBAL 并拷贝字节（不占用剪贴板锁）。</summary>
    private static IntPtr Allocate(byte[] data)
    {
        var handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)data.Length);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"分配剪贴板内存失败（{data.Length} 字节）");
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            throw new InvalidOperationException("锁定剪贴板内存失败");
        }

        try
        {
            Marshal.Copy(data, 0, pointer, data.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }

        return handle;
    }

    /// <summary>分配 HGLOBAL 并写入 UTF-16 字符串（含结尾 NUL）。</summary>
    private static IntPtr AllocateUnicodeText(string value)
    {
        var chars = value.ToCharArray();
        var byteCount = ((long)chars.Length + 1) * 2;
        if (byteCount > int.MaxValue)
        {
            throw new InvalidOperationException("文本过长，无法写入剪贴板");
        }

        var handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)byteCount);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"分配剪贴板内存失败（{byteCount} 字节）");
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            throw new InvalidOperationException("锁定剪贴板内存失败");
        }

        try
        {
            Marshal.Copy(chars, 0, pointer, chars.Length);
            Marshal.WriteInt16(pointer, chars.Length * 2, 0);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }

        return handle;
    }

    private static void FreeAll(List<(uint Format, IntPtr Handle)> blocks)
    {
        foreach (var (_, handle) in blocks)
        {
            NativeMethods.GlobalFree(handle);
        }

        blocks.Clear();
    }
}
