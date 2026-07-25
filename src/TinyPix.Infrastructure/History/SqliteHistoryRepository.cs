using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TinyPix.Core.History;
using TinyPix.Core.Jobs;

namespace TinyPix.Infrastructure.History;

public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private readonly string _databasePath;
    private readonly int _recentLimit;
    private readonly int _historyLimit;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;

    public SqliteHistoryRepository(
        string databasePath,
        int recentLimit = 200,
        int historyLimit = 1000)
    {
        if (recentLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recentLimit));
        }

        if (historyLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(historyLimit));
        }

        _databasePath = Path.GetFullPath(databasePath);
        _recentLimit = recentLimit;
        _historyLimit = historyLimit;
    }

    public async Task AddRecentFileAsync(
        RecentFileEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO recent_files(path, last_tool_id, last_opened_utc)
            VALUES ($path, $tool, $opened)
            ON CONFLICT(path) DO UPDATE SET
                last_tool_id = excluded.last_tool_id,
                last_opened_utc = excluded.last_opened_utc;
            """,
            cancellationToken,
            ("$path", Path.GetFullPath(entry.Path)),
            ("$tool", (object?)entry.LastToolId ?? DBNull.Value),
            ("$opened", Format(entry.LastOpenedUtc))).ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            transaction,
            """
            DELETE FROM recent_files
            WHERE path NOT IN (
                SELECT path FROM recent_files
                ORDER BY last_opened_utc DESC
                LIMIT $limit
            );
            """,
            cancellationToken,
            ("$limit", _recentLimit)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RecentFileEntry>> GetRecentFilesAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT path, last_tool_id, last_opened_utc
            FROM recent_files
            ORDER BY last_opened_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, _recentLimit));

        var entries = new List<RecentFileEntry>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new RecentFileEntry(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                ParseDate(reader.GetString(2))));
        }

        return entries.AsReadOnly();
    }

    public async Task SaveHistoryAsync(
        HistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO job_history(
                job_id, tool_id, status, input_paths_json, output_paths_json,
                parameters_json, error_code, error_message, error_can_retry,
                created_utc, completed_utc)
            VALUES (
                $id, $tool, $status, $inputs, $outputs,
                $parameters, $errorCode, $errorMessage, $canRetry,
                $created, $completed)
            ON CONFLICT(job_id) DO UPDATE SET
                status = excluded.status,
                output_paths_json = excluded.output_paths_json,
                error_code = excluded.error_code,
                error_message = excluded.error_message,
                error_can_retry = excluded.error_can_retry,
                completed_utc = excluded.completed_utc;
            """,
            cancellationToken,
            ("$id", entry.JobId.ToString("D")),
            ("$tool", entry.ToolId),
            ("$status", (int)entry.Status),
            ("$inputs", JsonSerializer.Serialize(entry.InputPaths)),
            ("$outputs", JsonSerializer.Serialize(entry.OutputPaths)),
            ("$parameters", JsonSerializer.Serialize(entry.Parameters)),
            ("$errorCode", (object?)entry.Error?.Code ?? DBNull.Value),
            ("$errorMessage", (object?)entry.Error?.Message ?? DBNull.Value),
            ("$canRetry", entry.Error is null ? DBNull.Value : entry.Error.CanRetry ? 1 : 0),
            ("$created", Format(entry.CreatedUtc)),
            ("$completed", entry.CompletedUtc is null ? DBNull.Value : Format(entry.CompletedUtc.Value))).ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            transaction,
            """
            DELETE FROM job_history
            WHERE job_id NOT IN (
                SELECT job_id FROM job_history
                ORDER BY created_utc DESC
                LIMIT $limit
            );
            """,
            cancellationToken,
            ("$limit", _historyLimit)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT job_id, tool_id, status, input_paths_json, output_paths_json,
                   parameters_json, error_code, error_message, error_can_retry,
                   created_utc, completed_utc
            FROM job_history
            ORDER BY created_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, _historyLimit));

        var entries = new List<HistoryEntry>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            JobError? error = reader.IsDBNull(6)
                ? null
                : new JobError(reader.GetString(6), reader.GetString(7), reader.GetInt32(8) != 0);
            entries.Add(new HistoryEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                (JobStatus)reader.GetInt32(2),
                Deserialize<string[]>(reader.GetString(3)),
                Deserialize<string[]>(reader.GetString(4)),
                Deserialize<Dictionary<string, string>>(reader.GetString(5)),
                error,
                ParseDate(reader.GetString(9)),
                reader.IsDBNull(10) ? null : ParseDate(reader.GetString(10))));
        }

        return entries.AsReadOnly();
    }

    public async Task MarkActiveJobsInterruptedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            null,
            """
            UPDATE job_history
            SET status = $interrupted,
                error_code = 'job.interrupted',
                error_message = '应用上次退出时任务尚未完成',
                error_can_retry = 1,
                completed_utc = $completed
            WHERE status IN ($queued, $running, $cancelling);
            """,
            cancellationToken,
            ("$interrupted", (int)JobStatus.Interrupted),
            ("$completed", Format(DateTimeOffset.UtcNow)),
            ("$queued", (int)JobStatus.Queued),
            ("$running", (int)JobStatus.Running),
            ("$cancelling", (int)JobStatus.Cancelling)).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)
                ?? throw new InvalidOperationException("数据库路径缺少父目录。"));
            await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(
                connection,
                null,
                """
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;
                CREATE TABLE IF NOT EXISTS recent_files(
                    path TEXT PRIMARY KEY COLLATE NOCASE,
                    last_tool_id TEXT NULL,
                    last_opened_utc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_recent_files_opened
                    ON recent_files(last_opened_utc DESC);
                CREATE TABLE IF NOT EXISTS job_history(
                    job_id TEXT PRIMARY KEY,
                    tool_id TEXT NOT NULL,
                    status INTEGER NOT NULL,
                    input_paths_json TEXT NOT NULL,
                    output_paths_json TEXT NOT NULL,
                    parameters_json TEXT NOT NULL,
                    error_code TEXT NULL,
                    error_message TEXT NULL,
                    error_can_retry INTEGER NULL,
                    created_utc TEXT NOT NULL,
                    completed_utc TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_job_history_created
                    ON job_history(created_utc DESC);
                """,
                cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json)
        ?? throw new InvalidDataException("历史数据库包含无效 JSON。");

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
