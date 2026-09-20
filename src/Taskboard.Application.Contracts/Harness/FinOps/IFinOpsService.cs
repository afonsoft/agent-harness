using Taskboard.Agents;

namespace Taskboard.Harness.FinOps;

/// <summary>
/// FinOps control plane — records per-run/stage token usage, computes USD cost
/// from the price table and enforces budget caps
/// (SPEC-20260919-ade-observability-finops RF-001..RF-003).
/// </summary>
public interface IFinOpsService
{
    /// <summary>Exact cost for a usage sample under the configured rates.</summary>
    Task<decimal> ComputeCostAsync(string? modelName, TokenUsage usage, CancellationToken cancellationToken = default);

    /// <summary>Persists a cost metric row and returns it.</summary>
    Task<RunCostMetricDto> RecordUsageAsync(
        string runId,
        AgentType agentType,
        string? modelName,
        TokenUsage usage,
        string? stageKey = null,
        decimal? budgetCapUsd = null,
        CancellationToken cancellationToken = default);

    /// <summary>Cumulative USD cost recorded for a run (all stages).</summary>
    Task<decimal> GetCumulativeCostAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>Cumulative tokens recorded for a run (all stages).</summary>
    Task<long> GetCumulativeTokensAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>Aggregated summary for `last-7-days` | `last-30-days` | `all` (API contract §5).</summary>
    Task<FinOpsSummaryDto> GetSummaryAsync(string? period, CancellationToken cancellationToken = default);

    /// <summary>Per-run telemetry detail; null when the run has no metrics.</summary>
    Task<RunTelemetryDto?> GetRunTelemetryAsync(string runId, CancellationToken cancellationToken = default);
}
