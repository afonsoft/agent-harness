using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Dtos;

namespace Taskboard.Integrations.CliDb.Extractors;

/// <summary>
/// OpenCode: sessions + token/cost usage from
/// <c>~/.local/share/opencode/opencode.db → session</c>. Timestamps are epoch
/// milliseconds; <c>cost</c> is USD. Credential-bearing tables are denied in
/// <see cref="CliDatabaseMap"/> and never touched.
/// </summary>
public sealed class OpenCodeCliDbExtractor : CliDbExtractorBase
{
    // Baseline captured 2026-09-19 (user_version=0, application_id=0).
    private static readonly CliDbSchemaFingerprint Baseline = new(
        0, 0,
        "session(agent,cost,directory,id,metadata,model,parent_id,path,permission,project_id,revert," +
        "share_url,slug,summary_additions,summary_deletions,summary_diffs,summary_files,time_archived," +
        "time_compacting,time_created,time_updated,title,tokens_cache_read,tokens_cache_write,tokens_input," +
        "tokens_output,tokens_reasoning,version,workspace_id)");

    public OpenCodeCliDbExtractor(
        ICliDatabaseLocator locator, ICliDatabaseReader reader, ILogger<OpenCodeCliDbExtractor> logger)
        : base(locator, reader, logger)
    {
    }

    public override AgentCliKind Kind => AgentCliKind.OpenCode;
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
            "session",
            ["rowid", "id", "title", "time_created", "time_updated", "model",
             "tokens_input", "tokens_output", "tokens_cache_read", "tokens_cache_write", "cost"],
            r => (Rowid: r.GetInt64("rowid") ?? 0,
                Session: new CliSessionRecord(
                    source.Name,
                    r.GetString("id") ?? string.Empty,
                    r.GetString("title"),
                    CliDbTimestamps.EpochMs(r.GetInt64("time_created")),
                    CliDbTimestamps.OptEpochMs(r.GetInt64("time_updated")),
                    MessageCount: null,
                    r.GetString("model"),
                    r.GetInt64("tokens_input"),
                    r.GetInt64("tokens_output"),
                    (r.GetInt64("tokens_cache_read") ?? 0) + (r.GetInt64("tokens_cache_write") ?? 0),
                    // Vendor schema exposes real token counters — not estimated.
                    TokensEstimated: false),
                Usage: new CliUsageRecord(
                    source.Name,
                    r.GetString("id") ?? string.Empty,
                    r.GetString("id"),
                    CliDbTimestamps.EpochMs(r.GetInt64("time_updated")),
                    r.GetString("model"),
                    r.GetInt64("tokens_input"),
                    r.GetInt64("tokens_output"),
                    (r.GetInt64("tokens_cache_read") ?? 0) + (r.GetInt64("tokens_cache_write") ?? 0),
                    r.GetDouble("cost") is { } c ? (decimal)c : null)),
            whereClause: rowCursor is null ? null : "rowid > @cursor",
            parameters: rowCursor is null ? null : new Dictionary<string, object?> { ["@cursor"] = rowCursor },
            orderBy: "rowid",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var (rowid, session, usageRecord) in rows)
        {
            if (session.ExternalId.Length == 0)
            {
                continue;
            }
            sessions.Add(session);
            usage.Add(usageRecord);
            if (rowid > (maxRowid ?? 0))
            {
                maxRowid = rowid;
            }
        }

        return maxRowid;
    }
}
