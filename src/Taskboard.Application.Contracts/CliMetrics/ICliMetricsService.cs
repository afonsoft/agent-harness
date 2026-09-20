using Taskboard.Agents;
using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.CliMetrics;

/// <summary>
/// Ingestion + query surface for CLI metrics. Sync is incremental per source
/// (watermark + file signature skip), failure-isolated, and idempotent.
/// SPEC-20260919-cli-metrics RF-001..RF-005.
/// </summary>
public interface ICliMetricsService
{
    /// <summary>Runs one ingestion pass over every registered extractor source.</summary>
    Task<CliMetricsSyncResultDto> SyncAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CliMetricSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default);

    /// <param name="period"><c>7d|30d|90d|all</c> (default <c>30d</c>).</param>
    Task<CliMetricsSummaryDto> GetSummaryAsync(string? period, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CliSessionMetricDto>> GetSessionsAsync(
        AgentCliKind? kind, DateTime? fromUtc, DateTime? toUtc, int take, CancellationToken cancellationToken = default);
}
