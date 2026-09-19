using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Dtos;

namespace Taskboard.Integrations.CliDb.Extractors;

/// <summary>
/// Claude context-mode plugin (experimental): session metadata from
/// <c>~/.claude/context-mode/sessions/*.db → session_meta</c>.
/// Timestamps are <c>datetime('now')</c> text (<c>yyyy-MM-dd HH:mm:ss</c>, UTC).
/// </summary>
public sealed class ClaudeContextModeCliDbExtractor : CliDbExtractorBase
{
    // Baseline captured 2026-09-19 (user_version=0, application_id=0).
    private static readonly CliDbSchemaFingerprint Baseline = new(
        0, 0,
        "session_meta(compact_count,event_count,last_event_at,project_dir,session_id,started_at,usage_cursor)");

    public ClaudeContextModeCliDbExtractor(
        ICliDatabaseLocator locator, ICliDatabaseReader reader, ILogger<ClaudeContextModeCliDbExtractor> logger)
        : base(locator, reader, logger)
    {
    }

    public override AgentCliKind Kind => AgentCliKind.Claude;
    public override CliDbSchemaFingerprint ExpectedFingerprint => Baseline;
    protected override IReadOnlyList<CliDbSource> Sources => CliDatabaseMap.SourcesFor(Kind);

    protected override async Task<long?> ExtractSourceAsync(
        ICliDbConnection conn,
        string resolvedPath,
        CliDbSource source,
        long? rowCursor,
        List<CliSessionRecord> sessions,
        List<CliUsageRecord> usage,
        CancellationToken cancellationToken)
    {
        long? maxRowid = rowCursor;
        var rows = await conn.QueryAsync(
            "session_meta",
            ["rowid", "session_id", "started_at", "last_event_at", "event_count"],
            r => (Rowid: r.GetInt64("rowid") ?? 0,
                Record: new CliSessionRecord(
                    source.Name,
                    r.GetString("session_id") ?? string.Empty,
                    Title: null,
                    CliDbTimestamps.ParseText(r.GetString("started_at")) ?? DateTimeOffset.UnixEpoch,
                    CliDbTimestamps.ParseText(r.GetString("last_event_at")),
                    r.GetInt64("event_count") is { } n ? (int)n : null,
                    ModelName: null,
                    TokensInput: null, TokensOutput: null, TokensCached: null)),
            whereClause: rowCursor is null ? null : "rowid > @cursor",
            parameters: rowCursor is null ? null : new Dictionary<string, object?> { ["@cursor"] = rowCursor },
            orderBy: "rowid",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var (rowid, record) in rows)
        {
            if (record.ExternalId.Length == 0)
            {
                continue;
            }
            sessions.Add(record);
            if (rowid > (maxRowid ?? 0))
            {
                maxRowid = rowid;
            }
        }

        return maxRowid;
    }
}
