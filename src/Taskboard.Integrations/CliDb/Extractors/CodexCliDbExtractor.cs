using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Dtos;

namespace Taskboard.Integrations.CliDb.Extractors;

/// <summary>
/// Codex: session metadata from <c>~/.codex/state_*.sqlite → threads</c>.
/// Timestamps are epoch seconds. Codex exposes only a token total
/// (<c>tokens_used</c>) which does not map cleanly onto input/output — it is
/// carried as <c>TokensInput</c> with <c>TokensEstimated = true</c>
/// (SPEC-20260922-finops-cli-usage-breakdown RF-002).
/// </summary>
public sealed class CodexCliDbExtractor : CliDbExtractorBase
{
    // Baseline captured 2026-09-19 (user_version=0, application_id=0).
    private static readonly CliDbSchemaFingerprint Baseline = new(
        0, 0,
        "threads(agent_nickname,agent_path,agent_role,approval_mode,archived,archived_at,cli_version," +
        "created_at,created_at_ms,cwd,daybreak_enabled,first_user_message,git_branch,git_origin_url,git_sha," +
        "has_user_event,history_mode,id,is_pinned,memory_mode,model,model_provider,name,originator,preview," +
        "project_id,reasoning_effort,recency_at,recency_at_ms,rollout_path,sandbox_policy,section_entered_at_ms," +
        "section_position,source,thread_section_id,thread_source,title,tokens_used,updated_at,updated_at_ms)");

    public CodexCliDbExtractor(
        ICliDatabaseLocator locator, ICliDatabaseReader reader, ILogger<CodexCliDbExtractor> logger)
        : base(locator, reader, logger)
    {
    }

    public override AgentCliKind Kind => AgentCliKind.Codex;
    public override CliDbSchemaFingerprint ExpectedFingerprint => Baseline;
    public override IReadOnlyList<CliDbSource> Sources => CliDatabaseMap.SourcesFor(Kind);

    // v2: tokens_used flows into TokensInput (RF-002).
    public override int DataVersion => 2;

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
            "threads",
            ["rowid", "id", "title", "created_at", "updated_at", "model", "tokens_used"],
            r => (Rowid: r.GetInt64("rowid") ?? 0,
                Record: new CliSessionRecord(
                    source.Name,
                    r.GetString("id") ?? string.Empty,
                    r.GetString("title"),
                    CliDbTimestamps.EpochSeconds(r.GetInt64("created_at")),
                    CliDbTimestamps.OptEpochSeconds(r.GetInt64("updated_at")),
                    MessageCount: null,
                    r.GetString("model"),
                    // Vendor total without in/out split — still flagged estimated.
                    TokensInput: r.GetInt64("tokens_used"),
                    TokensOutput: null, TokensCached: null, TokensEstimated: true)),
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

/// <summary>Vendor timestamp normalization helpers (epoch s/ms, ISO-ish text).</summary>
internal static class CliDbTimestamps
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    public static DateTimeOffset EpochSeconds(long? v) =>
        v is > 0 ? DateTimeOffset.FromUnixTimeSeconds(v.Value) : Epoch;

    public static DateTimeOffset? OptEpochSeconds(long? v) =>
        v is > 0 ? DateTimeOffset.FromUnixTimeSeconds(v.Value) : null;

    public static DateTimeOffset EpochMs(long? v) =>
        v is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : Epoch;

    public static DateTimeOffset? OptEpochMs(long? v) =>
        v is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : null;

    public static DateTimeOffset? ParseText(string? v) =>
        DateTimeOffset.TryParse(v, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
