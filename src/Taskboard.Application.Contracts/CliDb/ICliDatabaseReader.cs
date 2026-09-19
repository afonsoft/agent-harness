using Taskboard.Agents;
using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.CliDb;

/// <summary>
/// Opens an external SQLite database strictly read-only; WAL/busy sources are
/// copied (db+wal+shm) to a temp dir which is deleted when the handle is
/// disposed. SPEC-20260919-cli-db-reader RF-002.
/// </summary>
public interface ICliDatabaseReader
{
    /// <summary>
    /// Opens <paramref name="absolutePath"/> (must resolve under <c>$HOME</c>)
    /// enforcing the whitelist/denylist declared by <paramref name="source"/>.
    /// </summary>
    Task<ICliDbConnection> OpenAsync(
        CliDbSource source,
        string absolutePath,
        CancellationToken cancellationToken = default);
}

/// <summary>A read-only connection to an external (possibly temp-copied) database.</summary>
public interface ICliDbConnection : IAsyncDisposable
{
    /// <summary>True when reads run against a temp copy of a WAL/busy source.</summary>
    bool CopiedToTemp { get; }

    /// <summary>
    /// Runs a structured SELECT against a whitelisted table. The implementation
    /// builds the SQL — <paramref name="table"/> and <paramref name="columns"/>
    /// must be strict identifiers, denied tables are rejected before execution,
    /// secret-named columns are dropped, and <paramref name="whereClause"/> may
    /// only reference whitelisted columns with parameterized values.
    /// </summary>
    Task<IReadOnlyList<T>> QueryAsync<T>(
        string table,
        IReadOnlyList<string> columns,
        Func<ICliDbRow, T> map,
        string? whereClause = null,
        IReadOnlyDictionary<string, object?>? parameters = null,
        string? orderBy = null,
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
