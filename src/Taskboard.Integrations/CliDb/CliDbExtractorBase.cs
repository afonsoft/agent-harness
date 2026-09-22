using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Dtos;

namespace Taskboard.Integrations.CliDb;

/// <summary>
/// Shared extraction flow: locate → open (ro/temp-copy) → fingerprint →
/// drift check → extractor-specific queries. One broken/drifted source never
/// affects the others and never throws into callers.
/// SPEC-20260919-cli-db-reader RF-003/RF-006.
/// </summary>
public abstract class CliDbExtractorBase : ICliDbExtractor
{
    private readonly ICliDatabaseLocator _locator;
    private readonly ICliDatabaseReader _reader;
    private readonly ILogger _logger;

    protected CliDbExtractorBase(
        ICliDatabaseLocator locator, ICliDatabaseReader reader, ILogger logger)
    {
        _locator = locator;
        _reader = reader;
        _logger = logger;
    }

    protected ILogger Logger => _logger;

    public abstract AgentCliKind Kind { get; }
    public abstract CliDbSchemaFingerprint ExpectedFingerprint { get; }

    /// <summary>
    /// Extractor data schema version — extractors that start emitting new
    /// fields (e.g. token estimates) bump this so the sync loop resets the
    /// watermark and re-extracts once (SPEC-20260922 RF-003).
    /// </summary>
    public virtual int DataVersion => 1;

    /// <summary>Sources this extractor reads — normally <see cref="CliDatabaseMap.SourcesFor"/>.</summary>
    public abstract IReadOnlyList<CliDbSource> Sources { get; }

    /// <summary>
    /// Whitelisted tables covered by drift detection — defaults to the whole
    /// whitelist. Estimation-only tables (e.g. Devin <c>message_nodes</c>)
    /// may be excluded so their drift degrades the extractor instead of
    /// blanking the source (SPEC-20260922 edge case).
    /// </summary>
    public virtual IReadOnlyList<string> DriftCheckedTables(CliDbSource source) =>
        source.WhitelistTables;

    /// <summary>
    /// Extractor-specific whitelisted queries for one opened database file.
    /// <paramref name="rowCursor"/> resumes inside this file (rowid watermark);
    /// return the greatest rowid consumed so the caller can build the next cursor.
    /// </summary>
    protected abstract Task<long?> ExtractSourceAsync(
        ICliDbConnection conn,
        string resolvedPath,
        CliDbSource source,
        long? rowCursor,
        List<CliSessionRecord> sessions,
        List<CliUsageRecord> usage,
        CancellationToken cancellationToken);

    /// <summary>Opaque watermark format shared by all extractors: "{resolvedPath}|{rowid}".</summary>
    protected static bool TryParseCursor(string? cursor, out string file, out long rowid)
    {
        file = string.Empty;
        rowid = 0;
        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        var sep = cursor.LastIndexOf('|');
        if (sep <= 0 || !long.TryParse(cursor[(sep + 1)..], out rowid))
        {
            return false;
        }

        file = cursor[..sep];
        return true;
    }

    protected static string FormatCursor(string file, long rowid) => $"{file}|{rowid}";

    // IN-clause chunking stays well under SQLite's variable limit.
    private const int RollupChunkSize = 500;

    /// <summary>
    /// Scalar-only rollup shared by estimation extractors:
    /// <c>SUM(length(column))</c> + <c>COUNT(*)</c> per
    /// <paramref name="groupColumn"/> value, restricted to the given values
    /// via chunked <c>IN</c> clauses. Returns <see langword="null"/> when the
    /// optional table is missing/drifted — callers degrade to null-token
    /// sessions instead of failing the pass (SPEC-20260922 RF-002).
    /// </summary>
    protected async Task<Dictionary<string, (long Chars, long Count)>?> TryRollupByGroupAsync(
        ICliDbConnection conn,
        string table,
        string groupColumn,
        string lengthColumn,
        IEnumerable<string> groupValues,
        CancellationToken cancellationToken)
    {
        try
        {
            var totals = new Dictionary<string, (long Chars, long Count)>(StringComparer.Ordinal);
            foreach (var chunk in groupValues.Distinct(StringComparer.Ordinal).Chunk(RollupChunkSize))
            {
                var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
                var placeholders = new List<string>(chunk.Length);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var name = $"@g{i}";
                    placeholders.Add(name);
                    parameters[name] = chunk[i];
                }

                var rows = await conn.QueryScalarRollupAsync(
                    table,
                    groupByColumn: groupColumn,
                    lengthColumns: [lengthColumn],
                    r => (Group: r.GetString(groupColumn) ?? string.Empty,
                        Chars: r.GetInt64($"len_{lengthColumn}") ?? 0,
                        Count: r.GetInt64("count_all") ?? 0),
                    whereClause: $"{groupColumn} IN ({string.Join(',', placeholders)})",
                    parameters: parameters,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                foreach (var row in rows)
                {
                    if (row.Group.Length == 0)
                    {
                        continue;
                    }
                    totals.TryGetValue(row.Group, out var agg);
                    totals[row.Group] = (agg.Chars + row.Chars, agg.Count + row.Count);
                }
            }
            return totals;
        }
        catch (Exception ex) when (ex is CliDbAccessDeniedException or CliDbReadException
            or Microsoft.Data.Sqlite.SqliteException)
        {
            // Optional estimation surface — a missing/drifted table must not
            // blank the sessions extraction.
            _logger.LogWarning(
                "{Kind} rollup on {Table} skipped ({Message}); sessions keep null tokens.",
                Kind, table, ex.Message);
            return null;
        }
    }

    public async Task<CliExtractionResult> ExtractSinceAsync(string? cursor, CancellationToken cancellationToken = default)
    {
        var sessions = new List<CliSessionRecord>();
        var usage = new List<CliUsageRecord>();
        var statuses = new List<CliDbSourceStatus>();
        var reasons = new List<string>();
        var copied = false;
        var sawDrift = false;
        var sawError = false;
        var sawAny = false;
        string? nextCursor = cursor;

        TryParseCursor(cursor, out var cursorFile, out var cursorRowid);
        var hasCursorFile = !string.IsNullOrEmpty(cursorFile);

        foreach (var source in Sources)
        {
            var paths = _locator.Resolve(source);
            if (paths.Count == 0)
            {
                statuses.Add(CliDbSourceStatus.Missing);
                continue;
            }

            foreach (var path in paths)
            {
                sawAny = true;
                // Resume semantics: skip files strictly before the cursor file.
                if (hasCursorFile && string.CompareOrdinal(path, cursorFile) < 0)
                {
                    continue;
                }

                try
                {
                    await using var conn = await _reader.OpenAsync(source, path, cancellationToken)
                        .ConfigureAwait(false);
                    copied |= conn.CopiedToTemp;

                    var fingerprint = await conn.GetSchemaFingerprintAsync(
                        DriftCheckedTables(source), cancellationToken).ConfigureAwait(false);
                    if (!CliDbSchemaFingerprinter.Matches(ExpectedFingerprint, fingerprint, out var diff))
                    {
                        // Schema metadata only — row content is never logged.
                        _logger.LogWarning(
                            "CliDb schema drift on {Kind}/{Source}: {Diff}", Kind, source.Name, diff);
                        reasons.Add($"{source.Name}: schema drifted ({diff})");
                        sawDrift = true;
                        continue;
                    }

                    var rowCursor = hasCursorFile && path == cursorFile ? cursorRowid : (long?)null;
                    var lastRowid = await ExtractSourceAsync(
                            conn, path, source, rowCursor, sessions, usage, cancellationToken)
                        .ConfigureAwait(false);
                    nextCursor = FormatCursor(path, lastRowid ?? rowCursor ?? 0);
                    statuses.Add(CliDbSourceStatus.Available);
                }
                catch (CliDbAccessDeniedException ex)
                {
                    _logger.LogWarning("CliDb access denied on {Kind}/{Source}: {Message}", Kind, source.Name, ex.Message);
                    reasons.Add($"{source.Name}: {ex.Message}");
                    sawError = true;
                }
                catch (CliDbReadException ex)
                {
                    _logger.LogWarning("CliDb read failed on {Kind}/{Source}: {Message}", Kind, source.Name, ex.Message);
                    reasons.Add($"{source.Name}: {ex.Message}");
                    sawError = true;
                }
            }
        }

        var status = ResolveAggregate(sawAny, sawDrift, sawError, copied, statuses);
        return new CliExtractionResult(sessions, usage, nextCursor, status,
            reasons.Count > 0 ? string.Join("; ", reasons) : null);
    }

    private static CliDbSourceStatus ResolveAggregate(
        bool sawAny, bool sawDrift, bool sawError, bool copied, List<CliDbSourceStatus> statuses)
    {
        if (!sawAny)
        {
            return CliDbSourceStatus.Missing;
        }
        if (statuses.Count == 0 && sawDrift && !sawError)
        {
            return CliDbSourceStatus.SchemaDrifted;
        }
        if (statuses.Count == 0 && sawError)
        {
            return CliDbSourceStatus.Error;
        }
        return copied ? CliDbSourceStatus.CopiedToTemp : CliDbSourceStatus.Available;
    }
}
