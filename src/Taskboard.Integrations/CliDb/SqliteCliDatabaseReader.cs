using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Dtos;

namespace Taskboard.Integrations.CliDb;

/// <summary>
/// Strictly read-only access to external CLI SQLite databases.
/// <see cref="SqliteOpenMode.ReadOnly"/> + <c>Pooling=false</c>; WAL/busy
/// sources are copied (db+wal+shm) to a temp dir that is always deleted on
/// dispose; a torn copy retries once before reporting failure.
/// SPEC-20260919-cli-db-reader RF-002/RF-004/RF-005.
/// </summary>
public sealed class SqliteCliDatabaseReader : ICliDatabaseReader
{
    private static readonly Regex IdentifierPattern =
        new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    // Word-ish boundary on '_' separators: catches access_token, api_key,
    // hashed_password, credential — but not metric columns like tokens_input.
    private static readonly Regex SecretColumnPattern =
        new("(^|_)(token|secret|key|credential|password|auth)(_|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UnsafeWherePattern =
        new(";|'|\"|--|/\\*|\\*/|@__limit", RegexOptions.Compiled);
    private static readonly Regex OrderByPattern =
        new("^[A-Za-z_][A-Za-z0-9_]*(\\s+(ASC|DESC))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _homeDirectory;
    private readonly ILogger<SqliteCliDatabaseReader> _logger;
    private readonly CliDbReadOptions _options;
    private readonly Action<string>? _onTempCopy;

    public SqliteCliDatabaseReader(
        string homeDirectory,
        ILogger<SqliteCliDatabaseReader> logger,
        CliDbReadOptions? options = null,
        Action<string>? onTempCopy = null)
    {
        _homeDirectory = Path.GetFullPath(homeDirectory);
        _logger = logger;
        _options = options ?? new CliDbReadOptions();
        _onTempCopy = onTempCopy;
    }

    public Task<ICliDbConnection> OpenAsync(
        CliDbSource source, string absolutePath, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(absolutePath);
        if (!IsUnderHome(path))
        {
            throw new CliDbAccessDeniedException($"Path outside $HOME rejected: {path}");
        }

        if (!File.Exists(path))
        {
            throw new CliDbReadException($"Database not found: {path}");
        }

        var length = new FileInfo(path).Length;
        if (length > _options.MaxFileSizeBytes)
        {
            throw new CliDbReadException(
                $"Database exceeds size cap ({length} > {_options.MaxFileSizeBytes} bytes): {path}");
        }

        var walExists = File.Exists(path + "-wal") || File.Exists(path + "-shm");
        if (walExists)
        {
            return Task.FromResult(OpenCopy(source, path));
        }

        try
        {
            var conn = OpenReadOnly(path);
            return Task.FromResult<ICliDbConnection>(
                new SqliteCliDbConnection(conn, source, _options, copiedToTemp: false, tempDir: null));
        }
        catch (SqliteException ex)
        {
            _logger.LogInformation(
                "Direct ro open failed for {Path} ({Code}); falling back to temp copy.",
                path, ex.SqliteErrorCode);
            return Task.FromResult(OpenCopy(source, path));
        }
    }

    /// <summary>Copies db/-wal/-shm to a temp dir and opens the copy. One retry on a torn copy.</summary>
    private ICliDbConnection OpenCopy(CliDbSource source, string path)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clidb-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                var fileName = Path.GetFileName(path);
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                {
                    var src = path + suffix;
                    if (File.Exists(src))
                    {
                        File.Copy(src, Path.Combine(tempDir, fileName + suffix), overwrite: true);
                    }
                }

                _onTempCopy?.Invoke(tempDir);
                var conn = OpenReadOnly(Path.Combine(tempDir, fileName));
                return new SqliteCliDbConnection(conn, source, _options, copiedToTemp: true, tempDir);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                TryDeleteDir(tempDir);
            }
        }

        throw new CliDbReadException($"Failed to read database via temp copy: {path} ({lastError?.Message})");
    }

    private SqliteConnection OpenReadOnly(string path)
    {
        var conn = new SqliteConnection(
            $"Data Source={path};Mode=ReadOnly;Pooling=false;Default Timeout={(int)_options.EffectiveCommandTimeout.TotalSeconds}");
        conn.Open();
        // Sanity probe — surfaces corrupt headers / non-sqlite files here.
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master";
        cmd.ExecuteScalar();
        return conn;
    }

    private bool IsUnderHome(string path) =>
        path.StartsWith(_homeDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private void TryDeleteDir(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete temp copy dir {Dir}.", dir);
        }
    }

    private sealed class SqliteCliDbConnection : ICliDbConnection
    {
        private readonly SqliteConnection _conn;
        private readonly CliDbSource _source;
        private readonly CliDbReadOptions _options;
        private readonly string? _tempDir;

        public SqliteCliDbConnection(
            SqliteConnection conn, CliDbSource source, CliDbReadOptions options,
            bool copiedToTemp, string? tempDir)
        {
            _conn = conn;
            _source = source;
            _options = options;
            CopiedToTemp = copiedToTemp;
            _tempDir = tempDir;
        }

        public bool CopiedToTemp { get; }

        public async Task<IReadOnlyList<T>> QueryAsync<T>(
            string table,
            IReadOnlyList<string> columns,
            Func<ICliDbRow, T> map,
            string? whereClause = null,
            IReadOnlyDictionary<string, object?>? parameters = null,
            string? orderBy = null,
            int? rowLimit = null,
            CancellationToken cancellationToken = default)
        {
            ValidateTable(table);
            foreach (var col in columns)
            {
                if (!IdentifierPattern.IsMatch(col))
                {
                    throw new CliDbAccessDeniedException($"Invalid column identifier: {col}");
                }
            }

            // Secret-named columns are excluded even when an extractor lists them (RF-004).
            var safeColumns = columns.Where(c => !SecretColumnPattern.IsMatch(c)).ToList();

            if (whereClause is not null && UnsafeWherePattern.IsMatch(whereClause))
            {
                throw new CliDbAccessDeniedException($"Unsafe where clause rejected: {whereClause}");
            }

            if (orderBy is not null && !OrderByPattern.IsMatch(orderBy))
            {
                throw new CliDbAccessDeniedException($"Invalid order by: {orderBy}");
            }

            var limit = rowLimit ?? _options.RowLimit;
            // `AS` pins the result name — on tables with an INTEGER PRIMARY KEY,
            // `SELECT "rowid"` is reported under the PK column's name (e.g.
            // `sequence`), which would break ordinal lookup in CliDbRow.
            var columnList = safeColumns.Count > 0
                ? string.Join(", ", safeColumns.Select(c => $"\"{c}\" AS \"{c}\""))
                : "1";
            var sql = $"SELECT {columnList} FROM \"{table}\"";
            if (whereClause is not null)
            {
                sql += $" WHERE {whereClause}";
            }
            if (orderBy is not null)
            {
                sql += $" ORDER BY {orderBy}";
            }
            sql += " LIMIT @__limit";

            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = (int)_options.EffectiveCommandTimeout.TotalSeconds;
            cmd.Parameters.AddWithValue("@__limit", limit);
            if (parameters is not null)
            {
                foreach (var (name, value) in parameters)
                {
                    cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
                }
            }

            var rows = new List<T>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(map(new CliDbRow(reader, safeColumns)));
            }

            return rows;
        }

        public async Task<IReadOnlyList<T>> QueryScalarRollupAsync<T>(
            string table,
            string? groupByColumn,
            IReadOnlyList<string> lengthColumns,
            Func<ICliDbRow, T> map,
            string? whereClause = null,
            IReadOnlyDictionary<string, object?>? parameters = null,
            int? rowLimit = null,
            CancellationToken cancellationToken = default)
        {
            ValidateTable(table);
            var selects = new List<string>();
            var outputNames = new List<string>();

            if (groupByColumn is not null)
            {
                ValidateRollupColumn(groupByColumn);
                selects.Add($"\"{groupByColumn}\" AS \"{groupByColumn}\"");
                outputNames.Add(groupByColumn);
            }

            foreach (var col in lengthColumns)
            {
                ValidateRollupColumn(col);
                var alias = $"len_{col}";
                selects.Add($"COALESCE(SUM(length(\"{col}\")), 0) AS \"{alias}\"");
                outputNames.Add(alias);
            }
            selects.Add("COUNT(*) AS \"count_all\"");
            outputNames.Add("count_all");

            if (whereClause is not null && UnsafeWherePattern.IsMatch(whereClause))
            {
                throw new CliDbAccessDeniedException($"Unsafe where clause rejected: {whereClause}");
            }

            var limit = rowLimit ?? _options.RowLimit;
            var sql = $"SELECT {string.Join(", ", selects)} FROM \"{table}\"";
            if (whereClause is not null)
            {
                sql += $" WHERE {whereClause}";
            }
            if (groupByColumn is not null)
            {
                sql += $" GROUP BY \"{groupByColumn}\" ORDER BY \"{groupByColumn}\"";
            }
            sql += " LIMIT @__limit";

            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = (int)_options.EffectiveCommandTimeout.TotalSeconds;
            cmd.Parameters.AddWithValue("@__limit", limit);
            if (parameters is not null)
            {
                foreach (var (name, value) in parameters)
                {
                    cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
                }
            }

            var rows = new List<T>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(map(new CliDbRow(reader, outputNames)));
            }

            return rows;
        }

        private void ValidateRollupColumn(string column)
        {
            if (!IdentifierPattern.IsMatch(column))
            {
                throw new CliDbAccessDeniedException($"Invalid column identifier: {column}");
            }
            // Secret-named columns are rejected — even their length must not
            // leak (SPEC-20260919 RF-004 carries over to scalar rollups).
            if (SecretColumnPattern.IsMatch(column))
            {
                throw new CliDbAccessDeniedException($"Secret-named column rejected: {column}");
            }
        }

        public async Task<CliDbSchemaFingerprint> GetSchemaFingerprintAsync(
            IReadOnlyList<string> whitelistedTables, CancellationToken cancellationToken = default)
        {
            var userVersion = await PragmaLongAsync("user_version", cancellationToken).ConfigureAwait(false);
            var appId = await PragmaLongAsync("application_id", cancellationToken).ConfigureAwait(false);

            var parts = new List<string>();
            foreach (var table in whitelistedTables.OrderBy(t => t, StringComparer.Ordinal))
            {
                if (!IdentifierPattern.IsMatch(table))
                {
                    continue;
                }

                await using var cmd = _conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
                var cols = new List<string>();
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    cols.Add(reader.GetString(1));
                }

                if (cols.Count > 0)
                {
                    cols.Sort(StringComparer.Ordinal);
                    parts.Add($"{table}({string.Join(',', cols)})");
                }
            }

            return new CliDbSchemaFingerprint(userVersion, appId, string.Join('|', parts));
        }

        private async Task<long> PragmaLongAsync(string pragma, CancellationToken cancellationToken)
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"PRAGMA {pragma}";
            var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is long l ? l : Convert.ToInt64(value);
        }

        private void ValidateTable(string table)
        {
            if (!IdentifierPattern.IsMatch(table))
            {
                throw new CliDbAccessDeniedException($"Invalid table identifier: {table}");
            }
            if (_source.DeniedTables.Contains(table, StringComparer.OrdinalIgnoreCase))
            {
                throw new CliDbAccessDeniedException($"Denied table queried: {table}");
            }
            if (!_source.WhitelistTables.Contains(table, StringComparer.OrdinalIgnoreCase))
            {
                throw new CliDbAccessDeniedException($"Table outside whitelist: {table}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _conn.DisposeAsync().ConfigureAwait(false);
            if (_tempDir is not null)
            {
                try
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
                catch
                {
                    // best-effort cleanup; temp dirs are inert
                }
            }
        }
    }

    private sealed class CliDbRow : ICliDbRow
    {
        private readonly IReadOnlyDictionary<string, object?> _values;

        public CliDbRow(SqliteDataReader reader, IReadOnlyList<string> selectedColumns)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in selectedColumns)
            {
                try
                {
                    var ordinal = reader.GetOrdinal(col);
                    values[col] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
                }
                catch (IndexOutOfRangeException)
                {
                    values[col] = null;
                }
            }
            _values = values;
        }

        public bool IsNull(string column) => !_values.TryGetValue(column, out var v) || v is null;

        public string? GetString(string column) =>
            _values.TryGetValue(column, out var v) ? v?.ToString() : null;

        public long? GetInt64(string column) =>
            _values.TryGetValue(column, out var v) && v is not null ? Convert.ToInt64(v) : null;

        public double? GetDouble(string column) =>
            _values.TryGetValue(column, out var v) && v is not null ? Convert.ToDouble(v) : null;
    }
}
