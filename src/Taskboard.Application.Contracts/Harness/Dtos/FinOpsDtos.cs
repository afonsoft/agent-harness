namespace Taskboard.Harness.FinOps;

/// <summary>Per-day burn for the FinOps dashboard.</summary>
public sealed record FinOpsDailyCostDto(DateOnly Date, decimal CostUsd, long TotalTokens);

/// <summary>Aggregated FinOps summary for a period (API contract §5).</summary>
public sealed record FinOpsSummaryDto(
    decimal TotalCostUsd,
    long TotalTokens,
    int RunsCount,
    IReadOnlyDictionary<string, decimal> CostByAgent,
    IReadOnlyDictionary<string, decimal> CostByModel,
    IReadOnlyList<FinOpsDailyCostDto> DailyCosts,
    CliUsageSummaryDto? CliUsage = null,
    FinOpsTokenWindowsDto? TokenWindows = null,
    IReadOnlyDictionary<string, double>? ModelUsage = null,
    IReadOnlyList<FinOpsActivityBinDto>? ActivityBins = null,
    IReadOnlyList<FinOpsSessionRowDto>? RecentSessions = null,
    IReadOnlyList<FinOpsAlertDto>? Alerts = null);

/// <summary>Usage/cost projected from ingested CLI session metrics (SPEC-20260920-harness-recurring-jobs RF-004).</summary>
public sealed record CliUsageSummaryDto(
    int Sessions,
    long TokensInput,
    long TokensOutput,
    long TokensCached,
    decimal CostUsd,
    IReadOnlyDictionary<string, decimal> CostByCli,
    /// <summary>Share (0-1) of sessions whose token counts are estimated, not vendor-reported.</summary>
    double EstimatedShare = 0.0,
    /// <summary>
    /// Per-CLI rows ordered Sessions desc / CostUsd desc / Cli asc — one entry
    /// per kind with aggregates in the period, zero values included
    /// (SPEC-20260922-finops-cli-usage-breakdown RF-001).
    /// </summary>
    IReadOnlyList<CliUsageByCliDto>? ByCli = null);

/// <summary>Per-CLI usage row for the FinOps breakdown table (SPEC-20260922 RF-001).</summary>
public sealed record CliUsageByCliDto(
    string Cli,
    int Sessions,
    long TokensInput,
    long TokensOutput,
    long TokensCached,
    decimal CostUsd,
    /// <summary>Share (0-1) of the CLI's sessions with estimated (not vendor) tokens.</summary>
    double EstimatedShare);

/// <summary>Token totals for the fixed 24h/7d/30d windows anchored at `now` (RF-001).</summary>
public sealed record FinOpsTokenWindowsDto(long Last24h, long Last7d, long Last30d);

/// <summary>One sparkline bucket — hour or day granularity (RF-003).</summary>
public sealed record FinOpsActivityBinDto(
    DateTimeOffset Start,
    int Sessions,
    int Messages,
    long Tokens);

/// <summary>Merged harness-run/CLI-session row for the recent-sessions table (RF-004).</summary>
public sealed record FinOpsSessionRowDto(
    string Source,
    string Id,
    string? Title,
    string? Model,
    /// <summary><c>running</c> when lastActivity is inside the active window, else <c>finished</c>.</summary>
    string Status,
    int? Messages,
    long Tokens,
    decimal? CostUsd,
    /// <summary>True when tokens or the projected cost are estimated (badge `~`).</summary>
    bool Estimated,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastActivityUtc);

/// <summary>Dashboard alert — severity <c>warn</c> | <c>crit</c> (RF-008).</summary>
public sealed record FinOpsAlertDto(
    string Severity,
    string Code,
    string Message,
    DateTimeOffset AtUtc);

/// <summary>Single recorded cost metric row.</summary>
public sealed record RunCostMetricDto(
    string RunId,
    string? StageKey,
    string AgentType,
    string? ModelName,
    long InputTokens,
    long OutputTokens,
    long CacheTokens,
    decimal CostUsd,
    DateTimeOffset RecordedAtUtc);

/// <summary>Per-run telemetry detail (API contract §5).</summary>
public sealed record RunTelemetryDto(
    string RunId,
    long TotalTokens,
    long InputTokens,
    long OutputTokens,
    long CacheTokens,
    decimal CostUsd,
    double? DurationSeconds,
    decimal? BudgetCapUsd,
    IReadOnlyList<RunCostMetricDto> Metrics);
