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
    private const string ColRowid = "rowid";
    private const string ColTitle = "title";
    private const string ColCreatedAt = "created_at";
    private const string ColLastActivityAt = "last_activity_at";
    private const string ColModel = "model";

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

    /// <summary>
    /// SPEC-20260929-webcli-toggle-finops-active-sessions RF-004: every pass
    /// re-reads sessions that are open (<c>last_activity_at IS NULL</c>) or
    /// active within this window, so a live session's end timestamp reaches
    /// the FinOps liveness rule instead of staying at first-ingest values.
    /// </summary>
    internal static readonly int RefreshWindowHours = 24;

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
            [ColRowid, "id", ColTitle, ColCreatedAt, ColLastActivityAt, ColModel],
            r => (Rowid: r.GetInt64(ColRowid) ?? 0,
                Record: new CliSessionRecord(
                    source.Name,
                    r.GetString("id") ?? string.Empty,
                    r.GetString(ColTitle),
                    CliDbTimestamps.EpochSeconds(r.GetInt64(ColCreatedAt)),
                    CliDbTimestamps.OptEpochSeconds(r.GetInt64(ColLastActivityAt)),
                    MessageCount: null,
                    r.GetString(ColModel),
                    // sessions.db has no usage columns — estimates come from
                    // the message_nodes rollup below.
                    TokensInput: null, TokensOutput: null, TokensCached: null, TokensEstimated: true)),
            whereClause: rowCursor is null ? null : "rowid > @cursor",
            parameters: rowCursor is null ? null : new Dictionary<string, object?> { ["@cursor"] = rowCursor },
            orderBy: ColRowid,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Refresh pass: the rowid watermark never re-reads an already-ingested
        // session, so an open session's last_activity_at would stay stale
        // forever. Re-read open/recently-active rows and merge them over the
        // incremental scan by external id (RF-004).
        var refreshSince = DateTimeOffset.UtcNow
            .AddHours(-RefreshWindowHours).ToUnixTimeSeconds();
        var refreshed = await conn.QueryAsync(
            "sessions",
            [ColRowid, "id", ColTitle, ColCreatedAt, ColLastActivityAt, ColModel],
            r => (Rowid: r.GetInt64(ColRowid) ?? 0,
                Record: new CliSessionRecord(
                    source.Name,
                    r.GetString("id") ?? string.Empty,
                    r.GetString(ColTitle),
                    CliDbTimestamps.EpochSeconds(r.GetInt64(ColCreatedAt)),
                    CliDbTimestamps.OptEpochSeconds(r.GetInt64(ColLastActivityAt)),
                    MessageCount: null,
                    r.GetString(ColModel),
                    TokensInput: null, TokensOutput: null, TokensCached: null, TokensEstimated: true)),
            whereClause: "last_activity_at IS NULL OR last_activity_at >= @since",
            parameters: new Dictionary<string, object?> { ["@since"] = refreshSince },
            orderBy: ColRowid,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var touchedById = new Dictionary<string, CliSessionRecord>(StringComparer.Ordinal);
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
            touchedById[record.ExternalId] = record;
        }
        foreach (var (_, record) in refreshed)
        {
            if (record.ExternalId.Length == 0)
            {
                continue;
            }
            touchedById[record.ExternalId] = record;
        }

        var touched = touchedById.Values.ToList();

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
