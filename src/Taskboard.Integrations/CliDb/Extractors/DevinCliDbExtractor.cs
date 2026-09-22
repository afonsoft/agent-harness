using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Dtos;

namespace Taskboard.Integrations.CliDb.Extractors;

/// <summary>
/// Devin CLI: session metadata from
/// <c>~/.local/share/devin/cli/sessions.db → sessions</c>.
/// Timestamps are epoch seconds; <c>last_activity_at</c> approximates the end.
/// Token estimates come from <c>message_nodes</c> via scalar-only rollups —
/// <c>SUM(length(chat_message))</c> + <c>COUNT(*)</c> per session; message
/// content is never selected (SPEC-20260922-finops-cli-usage-breakdown RF-002).
/// </summary>
public sealed class DevinCliDbExtractor : CliDbExtractorBase
{
    // Baseline captured 2026-09-19 (user_version=0, application_id=0) over the
    // drift-checked table only — message_nodes is an optional estimation
    // surface whose drift degrades to null-token sessions instead of
    // blanking the source (SPEC-20260922 edge case).
    private static readonly CliDbSchemaFingerprint Baseline = new(
        0, 0,
        "sessions(agent_mode,backend_type,cogs_json,created_at,hidden,id,last_activity_at,main_chain_id," +
        "metadata,model,shell_last_seen_index,title,working_directory,workspace_dirs)");

    private readonly CliTokenEstimator _estimator;

    public DevinCliDbExtractor(
        ICliDatabaseLocator locator, ICliDatabaseReader reader, ILogger<DevinCliDbExtractor> logger,
        CliTokenEstimator? estimator = null)
        : base(locator, reader, logger)
    {
        _estimator = estimator ?? new CliTokenEstimator();
    }

    public override AgentCliKind Kind => AgentCliKind.Devin;
    public override CliDbSchemaFingerprint ExpectedFingerprint => Baseline;
    public override IReadOnlyList<CliDbSource> Sources => CliDatabaseMap.SourcesFor(Kind);

    // v2: message_nodes scalar rollup feeds TokensInput + MessageCount (RF-002).
    public override int DataVersion => 2;

    // message_nodes is estimation-only — the sessions table alone decides drift.
    public override IReadOnlyList<string> DriftCheckedTables(CliDbSource source) => ["sessions"];

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
                    // sessions.db has no usage columns — estimates come from
                    // the message_nodes rollup below.
                    TokensInput: null, TokensOutput: null, TokensCached: null, TokensEstimated: true)),
            whereClause: rowCursor is null ? null : "rowid > @cursor",
            parameters: rowCursor is null ? null : new Dictionary<string, object?> { ["@cursor"] = rowCursor },
            orderBy: "rowid",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var touched = new List<CliSessionRecord>(rows.Count);
        foreach (var (rowid, record) in rows)
        {
            if (rowid > (maxRowid ?? 0))
            {
                maxRowid = rowid;
            }
            if (record.ExternalId.Length == 0)
            {
                continue;
            }
            touched.Add(record);
        }

        var rollup = await TryRollupByGroupAsync(
            conn, "message_nodes", "session_id", "chat_message",
            touched.Select(r => r.ExternalId), cancellationToken).ConfigureAwait(false);

        foreach (var record in touched)
        {
            if (rollup is not null && rollup.TryGetValue(record.ExternalId, out var node))
            {
                sessions.Add(record with
                {
                    MessageCount = (int)Math.Min(node.Count, int.MaxValue),
                    TokensInput = _estimator.FromChars(node.Chars),
                });
            }
            else
            {
                // Sessions without nodes keep null tokens — the FinOps card
                // renders them as "no usage data", not real zeros.
                sessions.Add(record);
            }
        }

        return maxRowid;
    }
}
