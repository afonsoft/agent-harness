using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Dtos;

namespace Taskboard.Integrations.CliDb.Extractors;

/// <summary>
/// Antigravity (agy): session metadata from
/// <c>~/.gemini/antigravity-cli/conversation_summaries.db → conversation_summaries</c>.
/// Timestamps are ISO-ish text (<c>yyyy-MM-dd HH:mm:ss.fffffff+00:00</c>).
/// Per-conversation DBs under <c>conversations/*.db</c> are registered for
/// status reporting but not read by the v1 extractor.
/// </summary>
public sealed class AntigravityCliDbExtractor : CliDbExtractorBase
{
    // Baseline captured 2026-09-19 (user_version=3, application_id=0).
    private static readonly CliDbSchemaFingerprint Baseline = new(
        3, 0,
        "conversation_summaries(agent_name,app_data_dir,battle_id,conversation_id,group_id,killed," +
        "last_modified_time,last_user_input_step_index,last_user_input_time,nesting_depth,not_fully_idle," +
        "parent_conversation_id,preview,project_id,raw_summary,source,status,step_count,title," +
        "winning_conversation_id,workspace_uris)");

    public AntigravityCliDbExtractor(
        ICliDatabaseLocator locator, ICliDatabaseReader reader, ILogger<AntigravityCliDbExtractor> logger)
        : base(locator, reader, logger)
    {
    }

    public override AgentCliKind Kind => AgentCliKind.Antigravity;
    public override CliDbSchemaFingerprint ExpectedFingerprint => Baseline;

    // v1 reads only the summaries DB — per-conversation files stay status-only.
    protected override IReadOnlyList<CliDbSource> Sources =>
        CliDatabaseMap.SourcesFor(Kind).Where(s => s.Name == "antigravity-summaries").ToList();

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
            "conversation_summaries",
            ["rowid", "conversation_id", "title", "last_modified_time", "step_count", "status"],
            r => (Rowid: r.GetInt64("rowid") ?? 0,
                Record: new CliSessionRecord(
                    source.Name,
                    r.GetString("conversation_id") ?? string.Empty,
                    r.GetString("title"),
                    // No creation timestamp exists — last activity approximates start.
                    CliDbTimestamps.ParseText(r.GetString("last_modified_time")) ?? DateTimeOffset.UnixEpoch,
                    CliDbTimestamps.ParseText(r.GetString("last_modified_time")),
                    r.GetInt64("step_count") is { } steps ? (int)steps : null,
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
