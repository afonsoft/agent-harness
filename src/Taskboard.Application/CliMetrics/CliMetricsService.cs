using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliDb;
using Taskboard.Application.Contracts.CliMetrics;
using Taskboard.Dtos;

namespace Taskboard.Application.CliMetrics;

/// <inheritdoc cref="ICliMetricsService"/>
public sealed class CliMetricsService : ICliMetricsService
{
    private const int MaxErrorLength = 500;

    private readonly ICliMetricsRepository _repository;
    private readonly IReadOnlyList<ICliDbExtractor> _extractors;
    private readonly ICliDatabaseLocator _locator;
    private readonly CliMetricsOptions _options;
    private readonly ILogger<CliMetricsService> _logger;
    private readonly TimeProvider _clock;

    public CliMetricsService(
        ICliMetricsRepository repository,
        IEnumerable<ICliDbExtractor> extractors,
        ICliDatabaseLocator locator,
        CliMetricsOptions options,
        ILogger<CliMetricsService> logger,
        TimeProvider? clock = null)
    {
        _repository = repository;
        _extractors = extractors.ToList();
        _locator = locator;
        _options = options;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<CliMetricsSyncResultDto> SyncAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var sourcesSynced = 0;
        var sessionsIngested = 0;
        var purged = 0;

        foreach (var extractor in _extractors)
        {
            try
            {
                var ingested = await SyncExtractorAsync(extractor, now, cancellationToken).ConfigureAwait(false);
                sessionsIngested += ingested;
            }
            catch (CliDbAccessDeniedException ex)
            {
                _logger.LogWarning("CLI metrics: {Kind} denied: {Message}", extractor.Kind, ex.Message);
                await MarkExtractorErrorAsync(extractor, "access denied", now, cancellationToken).ConfigureAwait(false);
            }
            catch (CliDbReadException ex)
            {
                _logger.LogWarning("CLI metrics: {Kind} read failed: {Message}", extractor.Kind, ex.Message);
                await MarkExtractorErrorAsync(extractor, ex.Message, now, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "CLI metrics: {Kind} sync failed", extractor.Kind);
                await MarkExtractorErrorAsync(extractor, ex.GetType().Name, now, cancellationToken).ConfigureAwait(false);
            }
            sourcesSynced++;
        }

        if (_options.RetentionDays > 0)
        {
            purged = await _repository.PurgeSessionsOlderThanAsync(
                now.AddDays(-_options.RetentionDays), cancellationToken).ConfigureAwait(false);
            if (purged > 0)
            {
                _logger.LogInformation("CLI metrics: purged {Count} raw session rows past {Days}d retention", purged, _options.RetentionDays);
            }
        }

        return new CliMetricsSyncResultDto("completed", InFlight: false, sourcesSynced, sessionsIngested, Error: null);
    }

    private async Task<int> SyncExtractorAsync(
        ICliDbExtractor extractor, DateTime now, CancellationToken cancellationToken)
    {
        var resolved = new List<(CliDbSource Source, IReadOnlyList<string> Paths)>();
        foreach (var source in extractor.Sources)
        {
            var paths = _locator.Resolve(source);
            if (paths.Count == 0)
            {
                await _repository.SaveSourceStateAsync(
                    extractor.Kind, source.Name, source.RelativePathPattern,
                    resolvedPaths: null, maxMtimeTicks: null, totalSizeBytes: null,
                    CliDbSourceStatus.Missing, watermarkCursor: null, rowCount: 0,
                    lastError: null, now, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                resolved.Add((source, paths));
            }
        }

        if (resolved.Count == 0)
        {
            return 0;
        }

        // File-signature skip: all files of all resolved sources unchanged → no-op.
        var signatures = new List<(CliDbSource Source, string JoinedPaths, long MaxMtimeTicks, long TotalSize)>();
        foreach (var (source, paths) in resolved)
        {
            long maxMtime = 0;
            long totalSize = 0;
            foreach (var path in paths.OrderBy(p => p, StringComparer.Ordinal))
            {
                var stat = _locator.Stat(path);
                if (stat is not null)
                {
                    maxMtime = Math.Max(maxMtime, stat.ModifiedUtc.Ticks);
                    totalSize += stat.SizeBytes;
                }
            }
            signatures.Add((source, string.Join(";", paths.OrderBy(p => p, StringComparer.Ordinal)), maxMtime, totalSize));
        }

        var unchanged = true;
        string? resumeCursor = null;
        foreach (var (source, joined, mtime, size) in signatures)
        {
            var state = await _repository.GetSourceStateAsync(extractor.Kind, source.Name, cancellationToken)
                .ConfigureAwait(false);
            resumeCursor ??= state?.WatermarkCursor;
            // Errored sources always retry — the failure may have been transient or
            // caused by a previous build, and an unchanged file must not pin it.
            if (state?.Status is CliDbSourceStatus.Error
                || state?.ResolvedPath != joined || state.FileModifiedUtc?.Ticks != mtime || state.FileSizeBytes != size)
            {
                unchanged = false;
            }
        }

        if (unchanged)
        {
            _logger.LogDebug("CLI metrics: {Kind} unchanged, skipping", extractor.Kind);
            return 0;
        }

        var result = await extractor.ExtractSinceAsync(resumeCursor, cancellationToken).ConfigureAwait(false);

        var ingested = 0;
        var days = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in result.Sessions.GroupBy(s => s.Source, StringComparer.Ordinal))
        {
            var source = extractor.Sources.FirstOrDefault(s => s.Name == group.Key);
            ingested += await _repository.UpsertSessionsAsync(
                extractor.Kind, group.Key, source?.RelativePathPattern ?? group.Key,
                group.ToList(), now, cancellationToken).ConfigureAwait(false);
            foreach (var session in group)
            {
                days.Add(session.StartedAtUtc.UtcDateTime.ToString("yyyy-MM-dd"));
            }
        }

        if (days.Count > 0)
        {
            await _repository.RecomputeAggregatesAsync(extractor.Kind, days, now, cancellationToken).ConfigureAwait(false);
        }

        var perSourceCounts = result.Sessions.GroupBy(s => s.Source, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (long)g.Count(), StringComparer.Ordinal);
        foreach (var (source, joined, mtime, size) in signatures)
        {
            await _repository.SaveSourceStateAsync(
                extractor.Kind, source.Name, source.RelativePathPattern, joined, mtime, size,
                result.Status, result.NextCursor,
                perSourceCounts.GetValueOrDefault(source.Name), result.Reason, now, cancellationToken)
                .ConfigureAwait(false);
        }

        return ingested;
    }

    private async Task MarkExtractorErrorAsync(
        ICliDbExtractor extractor, string error, DateTime now, CancellationToken cancellationToken)
    {
        var trimmed = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        foreach (var source in extractor.Sources)
        {
            await _repository.SaveSourceStateAsync(
                extractor.Kind, source.Name, source.RelativePathPattern,
                resolvedPaths: null, maxMtimeTicks: null, totalSizeBytes: null,
                CliDbSourceStatus.Error, watermarkCursor: null, rowCount: 0,
                trimmed, now, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<CliMetricSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default)
        => _repository.ListSourcesAsync(cancellationToken);

    public Task<CliMetricsSummaryDto> GetSummaryAsync(string? period, CancellationToken cancellationToken = default)
        => _repository.GetSummaryAsync(period ?? "30d", cancellationToken);

    public Task<IReadOnlyList<CliSessionMetricDto>> GetSessionsAsync(
        AgentCliKind? kind, DateTime? fromUtc, DateTime? toUtc, int take, CancellationToken cancellationToken = default)
        => _repository.GetSessionsAsync(kind, fromUtc, toUtc, take, cancellationToken);
}
