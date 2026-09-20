using Taskboard.Agents;

namespace Taskboard.Dtos;

/// <summary>Per-source ingestion status for the `/agents` UI + sync reports.</summary>
public sealed record CliMetricSourceDto(
    string Kind,
    string SourceName,
    string? Path,
    string Status,
    bool SchemaDrifted,
    DateTime? LastSyncUtc,
    string? LastError,
    long RowCount);

/// <summary>Persisted source sync state — watermark + last-seen file signature.</summary>
public sealed record CliMetricSourceStateDto(
    string? WatermarkCursor,
    string? ResolvedPath,
    DateTime? FileModifiedUtc,
    long? FileSizeBytes);

/// <summary>Session row for `GET /api/local/cli-metrics/sessions`.</summary>
public sealed record CliSessionMetricDto(
    string Kind,
    string Source,
    string ExternalId,
    string? Title,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    int? MessageCount,
    string? Model,
    long? InputTokens,
    long? OutputTokens,
    long? CachedTokens);

/// <summary>Aggregate bucket for summaries — LastActivityUtc drives the /agents badge.</summary>
public sealed record CliMetricsTotalsDto(
    long Sessions, long Messages, long Tokens, DateTime? LastActivityUtc = null);

/// <summary>Per-day rollup row.</summary>
public sealed record CliDayUsageDto(string Day, long Sessions, long Messages, long Tokens);

/// <summary>`GET /api/local/cli-metrics/summary?period=` response.</summary>
public sealed record CliMetricsSummaryDto(
    string Period,
    CliMetricsTotalsDto Totals,
    IReadOnlyDictionary<string, CliMetricsTotalsDto> ByKind,
    IReadOnlyList<CliDayUsageDto> ByDay);

/// <summary>Manual sync result (`POST /api/local/cli-metrics/sync`).</summary>
public sealed record CliMetricsSyncResultDto(
    string State,
    bool InFlight,
    int SourcesSynced,
    int SessionsIngested,
    string? Error);

/// <summary>Per-kind/per-model token+session totals for the FinOps feed (RF-007).</summary>
public sealed record CliUsageAggregateDto(
    string Kind,
    string? Model,
    long Sessions,
    long Messages,
    long TokensInput,
    long TokensOutput,
    long TokensCached);
