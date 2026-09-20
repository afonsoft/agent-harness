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
    IReadOnlyList<FinOpsDailyCostDto> DailyCosts);

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
