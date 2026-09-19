using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.CliDb;

/// <summary>
/// Opens an external SQLite database strictly read-only; WAL/busy sources are
/// copied (db+wal+shm) to a temp dir which is deleted when the handle is
/// disposed. SPEC-20260919-cli-db-reader RF-002.
/// </summary>
public interface ICliDatabaseReader
{
    Task<ICliDbConnection> OpenAsync(string absolutePath, CancellationToken cancellationToken = default);
}

/// <summary>A read-only connection to an external (possibly temp-copied) database.</summary>
public interface ICliDbConnection : IAsyncDisposable
{
    /// <summary>True when reads run against a temp copy of a WAL/busy source.</summary>
    bool CopiedToTemp { get; }

    /// <summary>
    /// Runs a whitelisted, parameterized query with row-limit and timeout budgets.
    /// Implementations must reject queries referencing denied tables.
    /// </summary>
    Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        Func<ICliDbRow, T> map,
        IReadOnlyDictionary<string, object?>? parameters = null,
        int? rowLimit = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fingerprint of the whitelisted schema surface for drift detection (RF-003).</summary>
    Task<CliDbSchemaFingerprint> GetSchemaFingerprintAsync(
        IReadOnlyList<string> whitelistedTables,
        CancellationToken cancellationToken = default);
}

/// <summary>Read abstraction over one result row — keeps ADO.NET types inside Integrations.</summary>
public interface ICliDbRow
{
    bool IsNull(string column);
    string? GetString(string column);
    long? GetInt64(string column);
    double? GetDouble(string column);
}
