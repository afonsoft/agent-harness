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

    public abstract AgentCliKind Kind { get; }
    public abstract CliDbSchemaFingerprint ExpectedFingerprint { get; }

    /// <summary>Sources this extractor reads — normally <see cref="CliDatabaseMap.SourcesFor"/>.</summary>
    protected abstract IReadOnlyList<CliDbSource> Sources { get; }

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
                        source.WhitelistTables, cancellationToken).ConfigureAwait(false);
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
