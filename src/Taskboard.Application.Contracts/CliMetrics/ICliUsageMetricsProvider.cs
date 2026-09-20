using Taskboard.Agents;
using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.CliMetrics;

/// <summary>
/// Contract-only feed for `ade-observability-finops` — per-kind/per-model
/// token and session aggregates over a date range. That spec merges this into
/// `RunCostMetric` rollups when it lands (SPEC-20260919-cli-metrics RF-007).
/// </summary>
public interface ICliUsageMetricsProvider
{
    Task<IReadOnlyList<CliUsageAggregateDto>> GetUsageAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CliUsageAggregateDto>> GetUsageAsync(
        AgentCliKind kind, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
