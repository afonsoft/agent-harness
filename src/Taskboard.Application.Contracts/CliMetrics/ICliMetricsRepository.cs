using Taskboard.Agents;
using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.CliMetrics;

/// <summary>
/// Persistence for CLI metrics ingestion — DTO-facing so the Integrations and
/// Server layers never touch domain entities. SPEC-20260919-cli-metrics.
/// </summary>
public interface ICliMetricsRepository
{
    /// <summary>Current sync state of a (kind, source) row, or null when never seen.</summary>
    Task<CliMetricSourceStateDto?> GetSourceStateAsync(
        AgentCliKind kind, string sourceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the (kind, source) row with the file signature seen this sync and
    /// the resulting status/watermark. <paramref name="resolvedPaths"/> is the
    /// `;`-joined file list (glob sources), <paramref name="maxMtimeTicks"/>/
    /// <paramref name="totalSizeBytes"/> the aggregate file signature.
    /// <paramref name="extractorDataVersion"/> persists the applied extractor
    /// data version — a bump clears the stored watermark (SPEC-20260922 RF-003);
    /// <see langword="null"/> keeps the stored version (error paths).
    /// </summary>
    Task SaveSourceStateAsync(
        AgentCliKind kind,
        string sourceName,
        string relativePath,
        string? resolvedPaths,
        long? maxMtimeTicks,
        long? totalSizeBytes,
        CliDbSourceStatus status,
        string? watermarkCursor,
        long rowCount,
        string? lastError,
        DateTime now,
        CancellationToken cancellationToken = default,
        int? extractorDataVersion = null);

    /// <summary>
    /// Dedupe-upserts extracted session rows under the (kind, source) row —
    /// existing ExternalIds update mutable fields instead of duplicating (RF-003).
    /// </summary>
    Task<int> UpsertSessionsAsync(
        AgentCliKind kind,
        string sourceName,
        string relativePath,
        IReadOnlyList<CliSessionRecord> records,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>Recomputes the (kind, day) aggregate buckets from raw session rows (RF-004).</summary>
    Task RecomputeAggregatesAsync(
        AgentCliKind kind, IReadOnlyCollection<string> days, DateTime now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CliMetricSourceDto>> ListSourcesAsync(CancellationToken cancellationToken = default);

    Task<CliMetricsSummaryDto> GetSummaryAsync(string period, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CliSessionMetricDto>> GetSessionsAsync(
        AgentCliKind? kind, DateTime? fromUtc, DateTime? toUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Deletes raw session rows older than the cutoff; aggregates stay (RF-008).</summary>
    Task<int> PurgeSessionsOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
