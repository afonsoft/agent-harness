using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Dtos;

namespace Taskboard.Integrations.CliDb.Extractors;

/// <summary>
/// Cline: session rollups from <c>~/.cline/data/db/hub-events-*.db → hub_events</c>,
/// grouped by <c>session_id</c> in memory (bounded by the row budget).
/// Timestamps are epoch milliseconds. <c>connectors.db</c> holds credentials and is
/// outside the glob + on the denylist.
/// </summary>
public sealed class ClineCliDbExtractor : CliDbExtractorBase
{
    // Baseline captured 2026-09-19 (user_version=0, application_id=0).
    private static readonly CliDbSchemaFingerprint Baseline = new(
        0, 0, "hub_events(created_at,envelope_json,event,sequence,session_id)");

    public ClineCliDbExtractor(
        ICliDatabaseLocator locator, ICliDatabaseReader reader, ILogger<ClineCliDbExtractor> logger)
        : base(locator, reader, logger)
    {
    }

    public override AgentCliKind Kind => AgentCliKind.Cline;
    public override CliDbSchemaFingerprint ExpectedFingerprint => Baseline;
    public override IReadOnlyList<CliDbSource> Sources => CliDatabaseMap.SourcesFor(Kind);

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
            "hub_events",
            ["rowid", "session_id", "created_at"],
            r => (Rowid: r.GetInt64("rowid") ?? 0,
                SessionId: r.GetString("session_id"),
                CreatedAt: r.GetInt64("created_at")),
            whereClause: rowCursor is null ? null : "rowid > @cursor",
            parameters: rowCursor is null ? null : new Dictionary<string, object?> { ["@cursor"] = rowCursor },
            orderBy: "rowid",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var grouped = new Dictionary<string, (long Min, int Count)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.SessionId))
            {
                continue;
            }
            var created = row.CreatedAt ?? 0;
            if (grouped.TryGetValue(row.SessionId, out var agg))
            {
                grouped[row.SessionId] = (Math.Min(agg.Min, created), agg.Count + 1);
            }
            else
            {
                grouped[row.SessionId] = (created, 1);
            }
            if (row.Rowid > (maxRowid ?? 0))
            {
                maxRowid = row.Rowid;
            }
        }

        foreach (var (sessionId, agg) in grouped.OrderBy(kv => kv.Value.Min))
        {
            sessions.Add(new CliSessionRecord(
                source.Name, sessionId, Title: null,
                CliDbTimestamps.EpochMs(agg.Min), EndedAtUtc: null,
                agg.Count, ModelName: null,
                TokensInput: null, TokensOutput: null, TokensCached: null));
        }

        return maxRowid;
    }
}
