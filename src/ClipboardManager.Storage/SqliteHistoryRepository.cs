using ClipboardManager.Core.Models;
using ClipboardManager.Core.Retention;
using Microsoft.Data.Sqlite;

namespace ClipboardManager.Storage;

/// <summary>入库结果。</summary>
/// <param name="Id">记录主键。</param>
/// <param name="IsNew">true 表示新增；false 表示命中内容哈希去重，只刷新了时间戳。</param>
/// <param name="UpdatedAt">刷新后的时间戳。</param>
public readonly record struct UpsertOutcome(long Id, bool IsNew, DateTimeOffset UpdatedAt);

/// <summary>淘汰结果。</summary>
/// <param name="RemovedCount">被删除的记录数。</param>
/// <param name="BlobPaths">被删除记录的本体文件相对路径（调用方负责删文件）。</param>
public readonly record struct EvictionResult(int RemovedCount, IReadOnlyList<string> BlobPaths);

/// <summary>
/// 历史记录仓储（SQLite；需求 §4.2 / §4.3）。
/// <para>
/// 设计取舍：进程内只保留<b>一条</b>长连接并由锁串行化 —— 剪贴板历史写入是低频操作，
/// 单写者模型可以彻底避开 "database is locked" 与多连接竞态。
/// </para>
/// </summary>
public sealed class SqliteHistoryRepository : IDisposable
{
    private readonly object _gate = new();
    private readonly string _connectionString;
    private SqliteConnection? _connection;
    private bool _disposed;

    /// <summary>创建仓储。</summary>
    /// <param name="databasePath">数据库文件绝对路径。</param>
    public SqliteHistoryRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    /// <summary>打开连接并建表（幂等）。</summary>
    public void Initialize()
    {
        lock (_gate)
        {
            var connection = EnsureConnection();

            using var command = connection.CreateCommand();
            // PRAGMA 说明（技术设计 §7.1）：
            // journal_mode=DELETE —— 不用 WAL：U 盘/网络盘上 -wal/-shm 残留会造成锁与数据丢失风险；
            // synchronous=NORMAL —— 便携设备上兼顾安全与写入量；
            // busy_timeout —— 偶发占用时等待而不是立刻抛错。
            command.CommandText = """
                PRAGMA journal_mode = DELETE;
                PRAGMA synchronous = NORMAL;
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 3000;
                """;
            command.ExecuteNonQuery();

            using var schema = connection.CreateCommand();
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS clip_items (
                  id           INTEGER PRIMARY KEY AUTOINCREMENT,
                  type         INTEGER NOT NULL,
                  text_content TEXT,
                  blob_path    TEXT,
                  file_paths   TEXT,
                  preview      TEXT NOT NULL,
                  content_hash TEXT NOT NULL,
                  size_bytes   INTEGER NOT NULL DEFAULT 0,
                  is_pinned    INTEGER NOT NULL DEFAULT 0,
                  source_app   TEXT,
                  created_at   INTEGER NOT NULL,
                  updated_at   INTEGER NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ux_clip_items_hash       ON clip_items(content_hash);
                CREATE INDEX        IF NOT EXISTS ix_clip_items_updated_at ON clip_items(updated_at DESC);
                CREATE INDEX        IF NOT EXISTS ix_clip_items_pinned     ON clip_items(is_pinned);
                PRAGMA user_version = 1;
                """;
            schema.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// 入库：按 <c>content_hash</c> 去重（需求 §3.3）——已存在则只刷新 <c>updated_at</c>，不新增行。
    /// </summary>
    /// <param name="candidate">剪贴板候选（已拷贝到托管内存的纯数据）。</param>
    /// <param name="contentHash">内容哈希。</param>
    /// <param name="preview">原文摘要（显示时再脱敏）。</param>
    /// <param name="blobPath">本体文件相对路径（图片 / HTML 有值）。</param>
    /// <param name="sizeBytes">入库体积（默认取候选体积；有本体时传本体字节数，供磁盘上限淘汰使用）。</param>
    public UpsertOutcome Upsert(
        ClipCandidate candidate,
        string contentHash,
        string preview,
        string? blobPath = null,
        long? sizeBytes = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrEmpty(contentHash);

        lock (_gate)
        {
            var connection = EnsureConnection();
            using var transaction = connection.BeginTransaction();

            using var find = connection.CreateCommand();
            find.Transaction = transaction;
            find.CommandText = "SELECT id FROM clip_items WHERE content_hash = $hash LIMIT 1;";
            find.Parameters.AddWithValue("$hash", contentHash);
            var existing = find.ExecuteScalar();

            var now = candidate.CapturedAt.ToUnixTimeSeconds();

            if (existing is not null and not DBNull)
            {
                var id = Convert.ToInt64(existing, System.Globalization.CultureInfo.InvariantCulture);
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE clip_items SET updated_at = $updated WHERE id = $id;";
                update.Parameters.AddWithValue("$updated", now);
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
                transaction.Commit();
                return new UpsertOutcome(id, IsNew: false, candidate.CapturedAt);
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO clip_items
                  (type, text_content, blob_path, file_paths, preview, content_hash, size_bytes, is_pinned, source_app, created_at, updated_at)
                VALUES
                  ($type, $text, $blob, $paths, $preview, $hash, $size, 0, $app, $created, $updated);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$type", (int)candidate.Type);
            insert.Parameters.AddWithValue("$text", (object?)candidate.Text ?? DBNull.Value);
            insert.Parameters.AddWithValue("$blob", (object?)blobPath ?? DBNull.Value);
            insert.Parameters.AddWithValue("$paths", string.Join('\n', candidate.FilePaths));
            insert.Parameters.AddWithValue("$preview", preview);
            insert.Parameters.AddWithValue("$hash", contentHash);
            insert.Parameters.AddWithValue("$size", sizeBytes ?? candidate.SizeBytes);
            insert.Parameters.AddWithValue("$app", (object?)candidate.SourceApp ?? DBNull.Value);
            insert.Parameters.AddWithValue("$created", now);
            insert.Parameters.AddWithValue("$updated", now);

            var idValue = Convert.ToInt64(insert.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            transaction.Commit();
            return new UpsertOutcome(idValue, IsNew: true, candidate.CapturedAt);
        }
    }

    /// <summary>按最近更新时间取记录（列表用）。</summary>
    /// <param name="limit">最多返回条数。</param>
    public IReadOnlyList<ClipItem> GetRecent(int limit)
    {
        lock (_gate)
        {
            var connection = EnsureConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, type, text_content, blob_path, file_paths, preview, content_hash,
                       size_bytes, is_pinned, source_app, created_at, updated_at
                FROM clip_items
                ORDER BY updated_at DESC, id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", Math.Max(limit, 0));

            var items = new List<ClipItem>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(Map(reader));
            }

            return items;
        }
    }

    /// <summary>删除单条记录（需求 §3.3 手动删除）。</summary>
    /// <param name="id">记录主键。</param>
    public bool Delete(long id)
    {
        lock (_gate)
        {
            var connection = EnsureConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM clip_items WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>删除全部记录（需求 §3.3 一键清空）。</summary>
    public int DeleteAll()
    {
        lock (_gate)
        {
            var connection = EnsureConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM clip_items;";
            return command.ExecuteNonQuery();
        }
    }

    /// <summary>记录总数。</summary>
    public int CountAll()
    {
        lock (_gate)
        {
            var connection = EnsureConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM clip_items;";
            return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>内容体积合计（字节），用于磁盘上限判定（D-09）。</summary>
    public long SumSizeBytes()
    {
        lock (_gate)
        {
            var connection = EnsureConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(SUM(size_bytes), 0) FROM clip_items;";
            return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 按条数上限淘汰（需求 §3.3 / 技术设计 §4.5）：只淘汰非收藏项，保留最近 <paramref name="maxItems"/> 条非收藏记录。
    /// 决策交给 <see cref="RetentionPlanner.PlanByCount"/>（纯函数，可单测），本方法只负责执行删除。
    /// </summary>
    /// <param name="maxItems">条数上限；-1 表示不限制。</param>
    public EvictionResult EnforceMaxItems(int maxItems)
    {
        if (maxItems < 0)
        {
            return new EvictionResult(0, []);
        }

        var doomed = RetentionPlanner.PlanByCount(GetRetentionCandidates(), maxItems);
        if (doomed.Count == 0)
        {
            return new EvictionResult(0, []);
        }

        return new EvictionResult(doomed.Count, DeleteMany(doomed));
    }

    /// <summary>
    /// 切换收藏状态（需求 §3.3：收藏项永不参与自动淘汰）。
    /// </summary>
    /// <param name="id">记录主键。</param>
    /// <param name="pinned">true 收藏，false 取消收藏。</param>
    public bool SetPinned(long id, bool pinned)
    {
        lock (_gate)
        {
            var connection = EnsureConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE clip_items SET is_pinned = $pinned WHERE id = $id;";
            command.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>取全部记录的淘汰决策信息（纯数据，供 <c>RetentionPlanner</c> 计算，不返回内容）。</summary>
    public IReadOnlyList<RetentionCandidate> GetRetentionCandidates()
    {
        lock (_gate)
        {
            var connection = EnsureConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, is_pinned, size_bytes, updated_at FROM clip_items;";

            var items = new List<RetentionCandidate>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new RetentionCandidate(
                    reader.GetInt64(0),
                    reader.GetInt32(1) != 0,
                    reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    reader.GetInt64(3)));
            }

            return items;
        }
    }

    /// <summary>
    /// 批量删除并返回被删记录的本体路径（调用方据此删文件，避免孤儿）。
    /// <para>
    /// 说明：<c>IN</c> 里的占位符是按<b>条数</b>生成的（来自 <c>ids.Count</c>，不含任何用户输入），
    /// 值一律走参数，不存在 SQL 注入面。
    /// </para>
    /// </summary>
    /// <param name="ids">要删除的记录主键。</param>
    public IReadOnlyList<string> DeleteMany(IReadOnlyList<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        lock (_gate)
        {
            var connection = EnsureConnection();
            using var transaction = connection.BeginTransaction();

            var placeholders = string.Join(", ", ids.Select(static (_, index) => "$id" + index));
            var blobPaths = new List<string>();

            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = $"SELECT blob_path FROM clip_items WHERE id IN ({placeholders});";
                AddIdParameters(select, ids);

                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                    {
                        blobPaths.Add(reader.GetString(0));
                    }
                }
            }

            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = $"DELETE FROM clip_items WHERE id IN ({placeholders});";
                AddIdParameters(delete, ids);
                delete.ExecuteNonQuery();
            }

            transaction.Commit();
            return blobPaths;
        }
    }

    private static void AddIdParameters(SqliteCommand command, IReadOnlyList<long> ids)
    {
        for (var index = 0; index < ids.Count; index++)
        {
            command.Parameters.AddWithValue("$id" + index, ids[index]);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _connection?.Dispose();
            _connection = null;
        }
    }

    /// <summary>
    /// 搜索（需求 §3.3 / 技术设计 §7.3）：对文本内容、文件路径、摘要做 <c>LIKE</c> 模糊匹配。
    /// 参数化 + 转义 <c>% _ \</c>，避免用户输入被当成通配符（也避免通配注入）。
    /// </summary>
    /// <param name="query">查询词；空查询退化为「最近记录」。</param>
    /// <param name="limit">最多返回条数。</param>
    public IReadOnlyList<ClipItem> Search(string? query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetRecent(limit);
        }

        lock (_gate)
        {
            var connection = EnsureConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, type, text_content, blob_path, file_paths, preview, content_hash,
                       size_bytes, is_pinned, source_app, created_at, updated_at
                FROM clip_items
                WHERE text_content LIKE $pattern ESCAPE '\'
                   OR file_paths   LIKE $pattern ESCAPE '\'
                   OR preview      LIKE $pattern ESCAPE '\'
                ORDER BY updated_at DESC, id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$pattern", "%" + EscapeLike(query.Trim()) + "%");
            command.Parameters.AddWithValue("$limit", Math.Max(limit, 0));

            var items = new List<ClipItem>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(Map(reader));
            }

            return items;
        }
    }

    /// <summary>按主键取单条记录（删除前读取用）。</summary>
    /// <param name="id">记录主键。</param>
    public ClipItem? GetById(long id)
    {
        lock (_gate)
        {
            var connection = EnsureConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, type, text_content, blob_path, file_paths, preview, content_hash,
                       size_bytes, is_pinned, source_app, created_at, updated_at
                FROM clip_items
                WHERE id = $id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", id);

            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }
    }

    /// <summary>
    /// 按内容哈希取记录（「删除即吊销」用：判断当前剪贴板里的内容是不是我们历史里的某条）。
    /// </summary>
    /// <param name="contentHash">内容哈希（与入库去重键同一口径）。</param>
    public ClipItem? FindByHash(string contentHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentHash);

        lock (_gate)
        {
            var connection = EnsureConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, type, text_content, blob_path, file_paths, preview, content_hash,
                       size_bytes, is_pinned, source_app, created_at, updated_at
                FROM clip_items
                WHERE content_hash = $hash
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$hash", contentHash);

            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }
    }

    /// <summary>取所有仍被引用的本体路径（供启动时清理孤儿文件）。</summary>
    public IReadOnlyList<string> GetReferencedBlobPaths()
    {
        lock (_gate)
        {
            var connection = EnsureConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT blob_path FROM clip_items WHERE blob_path IS NOT NULL;";

            var paths = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                {
                    paths.Add(reader.GetString(0));
                }
            }

            return paths;
        }
    }

    /// <summary>转义 LIKE 元字符（配合 <c>ESCAPE '\'</c> 使用）。</summary>
    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private SqliteConnection EnsureConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connection is not null)
        {
            return _connection;
        }

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        _connection = connection;
        return connection;
    }

    private static ClipItem Map(SqliteDataReader reader)
    {
        var filePaths = reader.IsDBNull(4)
            ? []
            : reader.GetString(4).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return new ClipItem
        {
            Id = reader.GetInt64(0),
            Type = (ClipContentType)reader.GetInt32(1),
            TextContent = reader.IsDBNull(2) ? null : reader.GetString(2),
            BlobPath = reader.IsDBNull(3) ? null : reader.GetString(3),
            FilePaths = filePaths,
            Preview = reader.GetString(5),
            ContentHash = reader.GetString(6),
            SizeBytes = reader.GetInt64(7),
            IsPinned = reader.GetInt32(8) != 0,
            SourceApp = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(10)),
            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(11)),
        };
    }
}
