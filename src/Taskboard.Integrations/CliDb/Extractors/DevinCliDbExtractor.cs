using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Dtos;

namespace Taskboard.Integrations.CliDb.Extractors;

/// <summary>
/// Devin CLI: session metadata from
/// <c>~/.local/share/devin/cli/sessions.db → sessions</c>.
/// Timestamps are epoch seconds; <c>last_activity_at</c> approximates the end.
/// </summary>
public sealed class DevinCliDbExtractor : CliDbExtractorBase
{
    // Baseline captured 2026-09-19 (user_version=0, application_id=0).
    private static readonly CliDbSchemaFingerprint Baseline = new(
        0, 0,
        "sessions(agent_mode,backend_type,cogs_json,created_at,hidden,id,last_activity_at,main_chain_id," +
        "metadata,model,shell_last_seen_index,title,working_directory,workspace_dirs)");

    public DevinCliDbExtractor(
        ICliDatabaseLocator locator, ICliDatabaseReader reader, ILogger<DevinCliDbExtractor> logger)
        : base(locator, reader, logger)
    {
    }

    public override AgentCliKind Kind => AgentCliKind.Devin;
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
            "sessions",
            ["rowid", "id", "title", "created_at", "last_activity_at", "model"],
            r => (Rowid: r.GetInt64("rowid") ?? 0,
                Record: new CliSessionRecord(
                    source.Name,
                    r.GetString("id") ?? string.Empty,
                    r.GetString("title"),
                    CliDbTimestamps.EpochSeconds(r.GetInt64("created_at")),
                    CliDbTimestamps.OptEpochSeconds(r.GetInt64("last_activity_at")),
                    MessageCount: null,
                    r.GetString("model"),
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
