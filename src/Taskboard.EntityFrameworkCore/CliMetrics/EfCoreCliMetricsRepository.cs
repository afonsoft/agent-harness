using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.Application.Contracts.CliMetrics;
using Taskboard.CliMetrics;
using Taskboard.Domain.Entities.CliMetrics;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.CliMetrics;

/// <inheritdoc cref="ICliMetricsRepository"/>
public sealed class EfCoreCliMetricsRepository : ICliMetricsRepository
{
    private const int BatchSize = 500;
    private readonly TaskboardDbContext _context;

    public EfCoreCliMetricsRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<CliMetricSourceStateDto?> GetSourceStateAsync(
        AgentCliKind kind, string sourceName, CancellationToken cancellationToken = default)
    {
        var row = await _context.CliMetricSources
            .AsNoTracking()
            .Where(s => s.Kind == kind && s.SourceName == sourceName)
            .Select(s => new CliMetricSourceStateDto(
                s.WatermarkCursor, s.ResolvedPath, s.FileModifiedUtc, s.FileSizeBytes, s.Status))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row;
    }

    public async Task SaveSourceStateAsync(
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
        CancellationToken cancellationToken = default)
    {
        var row = await _context.CliMetricSources
            .FirstOrDefaultAsync(s => s.Kind == kind && s.SourceName == sourceName, cancellationToken)
            .ConfigureAwait(false);
        row ??= _context.CliMetricSources
            .Add(CliMetricSource.Register(kind, sourceName, relativePath, now)).Entity;

        if (resolvedPaths is not null && maxMtimeTicks is not null && totalSizeBytes is not null)
        {
            row.RecordFileState(resolvedPaths, maxMtimeTicks.Value, totalSizeBytes.Value, now);
        }

        if (status == CliDbSourceStatus.Error)
        {
            row.MarkError(lastError ?? "unknown", now);
        }
        else if (status == CliDbSourceStatus.Missing)
        {
            row.MarkMissing(now);
        }
        else
        {
            row.MarkSync(status, watermarkCursor, rowCount, now);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> UpsertSessionsAsync(
        AgentCliKind kind,
        string sourceName,
        string relativePath,
        IReadOnlyList<CliSessionRecord> records,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var source = await _context.CliMetricSources
            .FirstOrDefaultAsync(s => s.Kind == kind && s.SourceName == sourceName, cancellationToken)
            .ConfigureAwait(false);
        if (source is null)
        {
            source = CliMetricSource.Register(kind, sourceName, relativePath, now);
            _context.CliMetricSources.Add(source);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var written = 0;
        foreach (var chunk in records.Chunk(BatchSize))
        {
            var externalIds = chunk.Select(r => r.ExternalId).ToList();
            var existing = await _context.CliSessionMetrics
                .Where(s => s.SourceId == source.Id && externalIds.Contains(s.ExternalId))
                .ToDictionaryAsync(s => s.ExternalId, cancellationToken)
                .ConfigureAwait(false);

            foreach (var record in chunk)
            {
                if (existing.TryGetValue(record.ExternalId, out var row))
                {
                    row.Update(record.Title, record.EndedAtUtc?.UtcDateTime, record.MessageCount, record.ModelName,
                        record.TokensInput, record.TokensOutput, record.TokensCached, now);
                }
                else
                {
                    _context.CliSessionMetrics.Add(CliSessionMetric.Create(
                        source.Id, kind, record.ExternalId, record.Title, record.StartedAtUtc.UtcDateTime,
                        record.EndedAtUtc?.UtcDateTime, record.MessageCount, record.ModelName,
                        record.TokensInput, record.TokensOutput, record.TokensCached, now));
                }
                written++;
            }

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    public async Task RecomputeAggregatesAsync(
        AgentCliKind kind, IReadOnlyCollection<string> days, DateTime now, CancellationToken cancellationToken = default)
    {
        foreach (var day in days)
        {
            if (!DateTime.TryParse(day, out var dayStart))
            {
                continue;
            }

            var start = DateTime.SpecifyKind(dayStart.Date, DateTimeKind.Utc);
            var end = start.AddDays(1);
            var sessions = await _context.CliSessionMetrics
                .Where(s => s.Kind == kind && s.StartedAtUtc >= start && s.StartedAtUtc < end)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var aggregate = await _context.CliDailyUsageAggregates
                .FirstOrDefaultAsync(a => a.Kind == kind && a.Day == day, cancellationToken)
                .ConfigureAwait(false);

            if (sessions.Count == 0)
            {
                if (aggregate is not null)
                {
                    _context.CliDailyUsageAggregates.Remove(aggregate);
                }
                continue;
            }

            // Recompute idempotently — survives re-ingestion and updates.
            if (aggregate is null)
            {
                aggregate = _context.CliDailyUsageAggregates
                    .Add(CliDailyUsageAggregate.Register(kind, day, now)).Entity;
            }
            else
            {
                aggregate.Reset(now);
            }

            var groups = sessions
                .GroupBy(s => s.ModelName ?? string.Empty)
                .Select(g => (Model: g.Key, Sessions: g.Count(), Messages: g.Sum(s => s.MessageCount ?? 0),
                    In: g.Sum(s => s.TokensInput ?? 0), Out: g.Sum(s => s.TokensOutput ?? 0),
                    Cached: g.Sum(s => s.TokensCached ?? 0)));

            foreach (var g in groups)
            {
                aggregate.Add(g.Sessions, g.Messages, g.In, g.Out, g.Cached,
                    g.Model.Length > 0 ? g.Model : null, now);
            }
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CliMetricSourceDto>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _context.CliMetricSources
            .AsNoTracking()
            .OrderBy(s => s.Kind).ThenBy(s => s.SourceName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Select(s => new CliMetricSourceDto(
                s.Kind.ToString(), s.SourceName, s.ResolvedPath, s.Status.ToString(),
                s.Status == CliDbSourceStatus.SchemaDrifted, s.LastSyncUtc, s.LastError, s.RowCount))
            .ToList();
    }

    public async Task<CliMetricsSummaryDto> GetSummaryAsync(string period, CancellationToken cancellationToken = default)
    {
        var cutoff = period switch
        {
            "7d" => DateTime.UtcNow.Date.AddDays(-7),
            "90d" => DateTime.UtcNow.Date.AddDays(-90),
            "all" => (DateTime?)null,
            _ => DateTime.UtcNow.Date.AddDays(-30),
        };

        var query = _context.CliSessionMetrics.AsNoTracking().AsQueryable();
        if (cutoff is not null)
        {
            query = query.Where(s => s.StartedAtUtc >= cutoff.Value);
        }

        var rows = await query
            .Select(s => new UsageRow(s.Kind, s.StartedAtUtc, s.MessageCount, s.TokensInput, s.TokensOutput, s.TokensCached))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        static CliMetricsTotalsDto Totals(IReadOnlyCollection<UsageRow> xs) => new(
            xs.Count,
            xs.Sum(x => (long)(x.MessageCount ?? 0)),
            xs.Sum(x => (long)((x.TokensInput ?? 0) + (x.TokensOutput ?? 0) + (x.TokensCached ?? 0))),
            xs.Count > 0 ? xs.Max(x => x.StartedAtUtc) : null);

        var totals = Totals(rows);
        var byKind = rows
            .GroupBy(r => r.Kind.ToString())
            .ToDictionary(g => g.Key, g => Totals(g.ToList()));
        var byDay = rows
            .GroupBy(r => r.StartedAtUtc.ToString("yyyy-MM-dd"))
            .OrderBy(g => g.Key)
            .Select(g => new CliDayUsageDto(g.Key, g.Count(),
                g.Sum(x => (long)(x.MessageCount ?? 0)),
                g.Sum(x => (long)((x.TokensInput ?? 0) + (x.TokensOutput ?? 0) + (x.TokensCached ?? 0)))))
            .ToList();

        return new CliMetricsSummaryDto(period ?? "30d", totals, byKind, byDay);
    }

    public async Task<IReadOnlyList<CliSessionMetricDto>> GetSessionsAsync(
        AgentCliKind? kind, DateTime? fromUtc, DateTime? toUtc, int take, CancellationToken cancellationToken = default)
    {
        var query = _context.CliSessionMetrics.AsNoTracking().AsQueryable();
        if (kind is not null)
        {
            query = query.Where(s => s.Kind == kind);
        }
        if (fromUtc is not null)
        {
            query = query.Where(s => s.StartedAtUtc >= fromUtc.Value);
        }
        if (toUtc is not null)
        {
            query = query.Where(s => s.StartedAtUtc <= toUtc.Value);
        }

        var rows = await query
            .OrderByDescending(s => s.StartedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Join(_context.CliMetricSources.AsNoTracking(),
                s => s.SourceId, src => src.Id,
                (s, src) => new { Metric = s, src.SourceName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Select(r => new CliSessionMetricDto(
                r.Metric.Kind.ToString(), r.SourceName, r.Metric.ExternalId, r.Metric.Title,
                r.Metric.StartedAtUtc, r.Metric.EndedAtUtc, r.Metric.MessageCount, r.Metric.ModelName,
                r.Metric.TokensInput, r.Metric.TokensOutput, r.Metric.TokensCached))
            .ToList();
    }

    public async Task<int> PurgeSessionsOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        return await _context.CliSessionMetrics
            .Where(s => s.StartedAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record UsageRow(
        AgentCliKind Kind, DateTime StartedAtUtc, int? MessageCount,
        long? TokensInput, long? TokensOutput, long? TokensCached);
}
