using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.CliMetrics;
using Taskboard.Domain.Agents;
using Taskboard.Domain.Entities.CliMetrics;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Harness;
using Taskboard.Harness.FinOps;
using Taskboard.Repositories;

namespace Taskboard.Application.Harness;

/// <summary>
/// FinOps control plane (SPEC-20260919-ade-observability-finops): records token
/// usage per run/stage, computes exact decimal costs from the seeded
/// <see cref="ModelPriceRate"/> table and aggregates the dashboard summaries.
/// SPEC-20260922-finops-dashboard-detail adds token windows, Source·Model
/// distribution, activity bins, merged recent sessions and alerts.
/// </summary>
public sealed class FinOpsService : IFinOpsService
{
    private const int RecentSessionsTake = 10;
    private const int MaxDayBins = 40;

    private readonly IRepository<RunCostMetric> _metrics;
    private readonly IRepository<ModelPriceRate> _rates;
    private readonly IRepository<CliDailyUsageAggregate> _cliAggregates;
    private readonly IRepository<CliSessionMetric> _cliSessions;
    private readonly IRepository<CliMetricSource> _cliSources;
    private readonly IRepository<AgentRun> _runs;
    private readonly FinOpsOptions _options;
    private readonly TimeProvider _clock;

    public FinOpsService(
        IRepository<RunCostMetric> metrics,
        IRepository<ModelPriceRate> rates,
        IRepository<CliDailyUsageAggregate> cliAggregates,
        IRepository<CliSessionMetric> cliSessions,
        IRepository<CliMetricSource> cliSources,
        IRepository<AgentRun> runs,
        FinOpsOptions? options = null,
        TimeProvider? clock = null)
    {
        _metrics = metrics;
        _rates = rates;
        _cliAggregates = cliAggregates;
        _cliSessions = cliSessions;
        _cliSources = cliSources;
        _runs = runs;
        _options = options ?? new FinOpsOptions();
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<decimal> ComputeCostAsync(string? modelName, TokenUsage usage, CancellationToken cancellationToken = default)
    {
        var rates = await LoadRatesAsync(cancellationToken).ConfigureAwait(false);
        return TokenCostCalculator.Calculate(rates, modelName, usage);
    }

    public async Task<RunCostMetricDto> RecordUsageAsync(
        string runId,
        AgentType agentType,
        string? modelName,
        TokenUsage usage,
        string? stageKey = null,
        decimal? budgetCapUsd = null,
        CancellationToken cancellationToken = default)
    {
        var cost = await ComputeCostAsync(modelName, usage, cancellationToken).ConfigureAwait(false);
        var metric = new RunCostMetric(
            Guid.NewGuid(), runId, agentType, modelName,
            usage.InputTokens, usage.OutputTokens, usage.CacheWriteTokens, usage.CacheReadTokens,
            cost, DateTime.UtcNow, stageKey, budgetCapUsd);

        await _metrics.AddAsync(metric, cancellationToken).ConfigureAwait(false);
        await _metrics.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(metric);
    }

    public async Task<decimal> GetCumulativeCostAsync(string runId, CancellationToken cancellationToken = default)
    {
        return await _metrics.Query
            .Where(m => m.RunId == runId)
            .SumAsync(m => (decimal?)m.CostUsd, cancellationToken)
            .ConfigureAwait(false) ?? 0m;
    }

    public async Task<long> GetCumulativeTokensAsync(string runId, CancellationToken cancellationToken = default)
    {
        return await _metrics.Query
            .Where(m => m.RunId == runId)
            .SumAsync(m => (long?)(m.InputTokens + m.OutputTokens + m.CacheWriteTokens + m.CacheReadTokens), cancellationToken)
            .ConfigureAwait(false) ?? 0L;
    }

    public async Task<FinOpsSummaryDto> GetSummaryAsync(string? period, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var since = period switch
        {
            "24h" => now.AddHours(-24),
            "last-7-days" => now.AddDays(-7),
            "all" => DateTime.MinValue,
            _ => now.AddDays(-30) // default: last-30-days
        };

        // RF-001 token windows are anchored at `now` regardless of the selected
        // period, so the raw scan always covers at least the last 30 days.
        var scanSince = since < now.AddDays(-30) ? since : now.AddDays(-30);

        var metrics = await _metrics.Query
            .Where(m => m.RecordedAtUtc >= scanSince)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var cliSessions = await _cliSessions.Query
            .Where(s => s.StartedAtUtc >= scanSince
                || (s.EndedAtUtc != null && s.EndedAtUtc >= scanSince))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var cliSources = await _cliSources.Query
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var runs = await _runs.Query
            .Where(r => r.StartedAt >= new DateTimeOffset(scanSince, TimeSpan.Zero)
                || (r.FinishedAt != null && r.FinishedAt >= new DateTimeOffset(scanSince, TimeSpan.Zero)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var rates = await LoadRatesAsync(cancellationToken).ConfigureAwait(false);

        var periodMetrics = metrics.Where(m => m.RecordedAtUtc >= since).ToList();
        var periodSessions = cliSessions
            .Where(s => s.StartedAtUtc >= since || (s.EndedAtUtc ?? s.StartedAtUtc) >= since)
            .ToList();

        // CLI usage projection (SPEC-20260920 RF-004) — the daily aggregate's
        // ISO day string compares lexicographically to the period cutoff.
        var sinceDay = since == DateTime.MinValue
            ? null
            : since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var cliRows = await _cliAggregates.Query
            .Where(a => sinceDay == null || string.Compare(a.Day, sinceDay) >= 0)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // SPEC-20260922 RF-001 — one row per kind with in-period aggregates,
        // zero values included; contractual ordering Sessions/CostUsd desc.
        var byCli = cliRows
            .GroupBy(r => r.Kind)
            .Select(g =>
            {
                var sessions = g.Sum(r => r.SessionsCount);
                var estimatedSessions = g.Where(r => r.TokensEstimated).Sum(r => r.SessionsCount);
                return new CliUsageByCliDto(
                    Cli: AgentCliMap.GetSpec(g.Key)?.DisplayName ?? g.Key.ToString(),
                    Sessions: sessions,
                    TokensInput: g.Sum(r => r.TokensInput),
                    TokensOutput: g.Sum(r => r.TokensOutput),
                    TokensCached: g.Sum(r => r.TokensCached),
                    CostUsd: g.Sum(r => r.CostUsd),
                    EstimatedShare: sessions == 0 ? 0.0 : (double)estimatedSessions / sessions);
            })
            .OrderByDescending(r => r.Sessions)
            .ThenByDescending(r => r.CostUsd)
            .ThenBy(r => r.Cli, StringComparer.Ordinal)
            .ToList();

        var cliUsage = cliRows.Count == 0
            ? null
            : new CliUsageSummaryDto(
                Sessions: cliRows.Sum(r => r.SessionsCount),
                TokensInput: cliRows.Sum(r => r.TokensInput),
                TokensOutput: cliRows.Sum(r => r.TokensOutput),
                TokensCached: cliRows.Sum(r => r.TokensCached),
                CostUsd: cliRows.Sum(r => r.CostUsd),
                CostByCli: cliRows
                    .GroupBy(r => r.Kind.ToString())
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.CostUsd)),
                EstimatedShare: cliRows.Sum(r => r.SessionsCount) == 0
                    ? 0.0
                    : (double)cliRows.Where(r => r.TokensEstimated).Sum(r => r.SessionsCount)
                        / cliRows.Sum(r => r.SessionsCount),
                ByCli: byCli);

        return new FinOpsSummaryDto(
            TotalCostUsd: periodMetrics.Sum(r => r.CostUsd),
            TotalTokens: periodMetrics.Sum(r => r.TotalTokens),
            RunsCount: periodMetrics.Select(r => r.RunId).Distinct().Count(),
            CostByAgent: periodMetrics
                .GroupBy(r => r.AgentType.ToString())
                .ToDictionary(g => g.Key, g => g.Sum(r => r.CostUsd)),
            CostByModel: periodMetrics
                .Where(r => r.ModelName is not null)
                .GroupBy(r => r.ModelName!)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.CostUsd)),
            DailyCosts: periodMetrics
                .GroupBy(r => DateOnly.FromDateTime(r.RecordedAtUtc))
                .OrderBy(g => g.Key)
                .Select(g => new FinOpsDailyCostDto(g.Key, g.Sum(r => r.CostUsd), g.Sum(r => r.TotalTokens)))
                .ToList(),
            CliUsage: cliUsage,
            TokenWindows: BuildTokenWindows(now, metrics, cliSessions),
            ModelUsage: BuildModelUsage(periodMetrics, periodSessions),
            ActivityBins: BuildActivityBins(since, now, cliSessions, metrics),
            RecentSessions: BuildRecentSessions(now, cliSessions, periodMetrics, runs, rates),
            Alerts: BuildAlerts(now, since, cliSessions, metrics, cliSources, runs, cliRows));
    }

    // RF-001 — fixed 24h/7d/30d windows anchored at `now`, combining harness
    // metric tokens with CLI session tokens over the retained raw rows.
    private static FinOpsTokenWindowsDto BuildTokenWindows(
        DateTime now, IReadOnlyList<RunCostMetric> metrics, IReadOnlyList<CliSessionMetric> sessions)
    {
        long Tokens(DateTime cutoff) =>
            metrics.Where(m => m.RecordedAtUtc >= cutoff).Sum(m => m.TotalTokens)
            + sessions.Where(s => s.StartedAtUtc >= cutoff).Sum(SessionTokens);

        return new FinOpsTokenWindowsDto(
            Tokens(now.AddHours(-24)), Tokens(now.AddDays(-7)), Tokens(now.AddDays(-30)));
    }

    private static long SessionTokens(CliSessionMetric s) =>
        (s.TokensInput ?? 0) + (s.TokensOutput ?? 0) + (s.TokensCached ?? 0);

    // RF-002 — "% of sessions" distribution keyed "Source · Model"; sparse
    // model names collapse into (unknown).
    private static IReadOnlyDictionary<string, double> BuildModelUsage(
        IReadOnlyList<RunCostMetric> periodMetrics, IReadOnlyList<CliSessionMetric> periodSessions)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        // Harness runs count once per RunId; model = latest metric's model.
        foreach (var run in periodMetrics.GroupBy(m => m.RunId))
        {
            var model = NormalizeModel(run.OrderBy(m => m.RecordedAtUtc).Last().ModelName);
            var key = $"Harness · {model}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        foreach (var session in periodSessions)
        {
            var source = AgentCliMap.GetSpec(session.Kind)?.DisplayName ?? session.Kind.ToString();
            var key = $"{source} · {NormalizeModel(session.ModelName)}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var total = counts.Values.Sum();
        return total == 0
            ? new Dictionary<string, double>(StringComparer.Ordinal)
            : counts.ToDictionary(kv => kv.Key, kv => kv.Value * 100.0 / total, StringComparer.Ordinal);
    }

    private static string NormalizeModel(string? modelName)
    {
        var (_, model) = CliModelName.Normalize(modelName);
        return string.IsNullOrWhiteSpace(model) || model is "(unset)" or "<synthetic>"
            ? "(unknown)"
            : model;
    }

    // RF-003 — hour bins when the period span ≤ 2 days, day bins otherwise,
    // capped at 40. Empty bins are zero-filled for a continuous series.
    private static IReadOnlyList<FinOpsActivityBinDto> BuildActivityBins(
        DateTime since, DateTime now,
        IReadOnlyList<CliSessionMetric> sessions, IReadOnlyList<RunCostMetric> metrics)
    {
        var span = now - since;
        DateTime binStart;
        TimeSpan binWidth;
        int binCount;
        if (span <= TimeSpan.FromDays(2))
        {
            binCount = 24;
            binWidth = span / 24;
            binStart = since;
        }
        else if (span <= TimeSpan.FromDays(MaxDayBins))
        {
            binCount = Math.Max(1, (int)Math.Ceiling(span.TotalDays));
            binWidth = TimeSpan.FromDays(1);
            binStart = now.AddDays(-binCount);
        }
        else
        {
            // `all` (or very long spans): fixed trailing window of 40 days —
            // older activity is excluded from the sparkline but counted in totals.
            binCount = MaxDayBins;
            binWidth = TimeSpan.FromDays(1);
            binStart = now.AddDays(-MaxDayBins);
        }

        var sessionsPerBin = new int[binCount];
        var messagesPerBin = new int[binCount];
        var tokensPerBin = new long[binCount];
        var runIdsPerBin = new HashSet<string>[binCount];
        for (var i = 0; i < binCount; i++)
        {
            runIdsPerBin[i] = new HashSet<string>(StringComparer.Ordinal);
        }

        int BinIndex(DateTime t)
        {
            var idx = (int)((t - binStart) / binWidth);
            return idx < 0 || idx >= binCount ? -1 : idx;
        }

        foreach (var session in sessions)
        {
            var idx = BinIndex(session.EndedAtUtc ?? session.StartedAtUtc);
            if (idx < 0)
            {
                continue;
            }
            sessionsPerBin[idx]++;
            messagesPerBin[idx] += session.MessageCount ?? 0;
            tokensPerBin[idx] += SessionTokens(session);
        }

        foreach (var metric in metrics)
        {
            var idx = BinIndex(metric.RecordedAtUtc);
            if (idx < 0)
            {
                continue;
            }
            runIdsPerBin[idx].Add(metric.RunId);
            tokensPerBin[idx] += metric.TotalTokens;
        }

        var bins = new List<FinOpsActivityBinDto>(binCount);
        for (var i = 0; i < binCount; i++)
        {
            bins.Add(new FinOpsActivityBinDto(
                new DateTimeOffset(binStart + i * binWidth, TimeSpan.Zero),
                sessionsPerBin[i] + runIdsPerBin[i].Count,
                messagesPerBin[i],
                tokensPerBin[i]));
        }
        return bins;
    }

    // RF-004 — merged top-10 of CLI sessions + harness runs; status derives
    // from lastActivity inside the configured active window.
    private IReadOnlyList<FinOpsSessionRowDto> BuildRecentSessions(
        DateTime now,
        IReadOnlyList<CliSessionMetric> sessions,
        IReadOnlyList<RunCostMetric> periodMetrics,
        IReadOnlyList<AgentRun> runs,
        IReadOnlyList<ModelPriceRateInfo> rates)
    {
        var activeWindow = TimeSpan.FromSeconds(_options.ActiveWindowSeconds);
        string Status(DateTime lastActivity) => now - lastActivity <= activeWindow ? "running" : "finished";

        var runsById = new Dictionary<string, AgentRun>(StringComparer.Ordinal);
        foreach (var run in runs)
        {
            runsById[run.Id.ToString("N")] = run;
            runsById[run.Id.ToString("D")] = run;
        }

        var rows = new List<FinOpsSessionRowDto>();
        foreach (var s in sessions)
        {
            var lastActivity = s.EndedAtUtc ?? s.StartedAtUtc;
            var tokens = SessionTokens(s);
            var (_, model) = CliModelName.Normalize(s.ModelName);
            // Fallback-priced costs (no matching rate) carry the same ~ badge.
            var costEstimated = s.CostUsd is > 0 && tokens > 0
                && TokenCostCalculator.ResolveSpecific(rates, model) is null;
            rows.Add(new FinOpsSessionRowDto(
                Source: AgentCliMap.GetSpec(s.Kind)?.DisplayName ?? s.Kind.ToString(),
                Id: s.ExternalId,
                Title: s.Title,
                Model: NormalizeModel(s.ModelName),
                Status: Status(lastActivity),
                Messages: s.MessageCount,
                Tokens: tokens,
                CostUsd: s.CostUsd,
                Estimated: s.TokensEstimated || costEstimated,
                StartedAtUtc: new DateTimeOffset(s.StartedAtUtc, TimeSpan.Zero),
                LastActivityUtc: new DateTimeOffset(lastActivity, TimeSpan.Zero)));
        }

        foreach (var run in periodMetrics.GroupBy(m => m.RunId))
        {
            var ordered = run.OrderBy(m => m.RecordedAtUtc).ToList();
            var lastActivity = ordered[^1].RecordedAtUtc;
            runsById.TryGetValue(run.Key, out var agentRun);
            rows.Add(new FinOpsSessionRowDto(
                Source: "Harness",
                Id: run.Key,
                Title: agentRun?.IssueId,
                Model: NormalizeModel(ordered[^1].ModelName),
                Status: Status(lastActivity),
                Messages: null,
                Tokens: ordered.Sum(m => m.TotalTokens),
                CostUsd: ordered.Sum(m => m.CostUsd),
                Estimated: false,
                StartedAtUtc: new DateTimeOffset(
                    agentRun?.StartedAt.UtcDateTime ?? ordered[0].RecordedAtUtc, TimeSpan.Zero),
                LastActivityUtc: new DateTimeOffset(lastActivity, TimeSpan.Zero)));
        }

        return rows
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.LastActivityUtc)
            .ThenBy(r => r.Status == "running" ? 0 : 1)
            .Take(RecentSessionsTake)
            .ToList();
    }

    // RF-008 — warn/crit alerts computed server-side.
    private IReadOnlyList<FinOpsAlertDto> BuildAlerts(
        DateTime now,
        DateTime since,
        IReadOnlyList<CliSessionMetric> sessions,
        IReadOnlyList<RunCostMetric> metrics,
        IReadOnlyList<CliMetricSource> sources,
        IReadOnlyList<AgentRun> runs,
        IReadOnlyList<CliDailyUsageAggregate> cliRows)
    {
        var atUtc = new DateTimeOffset(now, TimeSpan.Zero);
        var alerts = new List<FinOpsAlertDto>();

        var windowStart = now.AddSeconds(-_options.ActiveWindowSeconds);
        var anyActive = sessions.Any(s => (s.EndedAtUtc ?? s.StartedAtUtc) >= windowStart)
            || metrics.Any(m => m.RecordedAtUtc >= windowStart);
        if (!anyActive)
        {
            alerts.Add(new FinOpsAlertDto("warn", "NoActiveSessions",
                $"No active sessions in the last {_options.ActiveWindowSeconds / 60} minutes", atUtc));
        }

        foreach (var source in sources.Where(
            s => s.Status is CliDbSourceStatus.Error or CliDbSourceStatus.SchemaDrifted))
        {
            var drifted = source.Status == CliDbSourceStatus.SchemaDrifted;
            var detail = source.LastError is { Length: > 0 } err ? $" — {err}" : string.Empty;
            alerts.Add(new FinOpsAlertDto(
                drifted ? "warn" : "crit",
                drifted ? "SourceSchemaDrifted" : "SourceError",
                $"CLI source {source.Kind}/{source.SourceName}: {source.Status}{detail}", atUtc));
        }

        foreach (var run in runs.Where(r => r.State == AgentRunState.BudgetExceeded
            && (r.FinishedAt?.UtcDateTime ?? r.StartedAt.UtcDateTime) >= since))
        {
            alerts.Add(new FinOpsAlertDto("crit", "BudgetExceeded",
                $"Run {run.IssueId} exceeded its budget cap", atUtc));
        }

        // (d) latest day cost > 2× the period daily average (strictly greater).
        var dailyCost = new Dictionary<DateOnly, decimal>();
        foreach (var m in metrics.Where(m => m.RecordedAtUtc >= since))
        {
            var day = DateOnly.FromDateTime(m.RecordedAtUtc);
            dailyCost[day] = dailyCost.GetValueOrDefault(day) + m.CostUsd;
        }
        foreach (var agg in cliRows)
        {
            if (DateOnly.TryParse(agg.Day, CultureInfo.InvariantCulture, out var day))
            {
                dailyCost[day] = dailyCost.GetValueOrDefault(day) + agg.CostUsd;
            }
        }
        if (dailyCost.Count > 0)
        {
            // `all` has no fixed span — the average runs from the first day with data.
            var periodDays = since > DateTime.MinValue
                ? Math.Max(1, (int)Math.Ceiling((now - since).TotalDays))
                : Math.Max(1, DateOnly.FromDateTime(now).DayNumber - dailyCost.Keys.Min().DayNumber + 1);
            var average = dailyCost.Values.Sum() / periodDays;
            var latest = dailyCost.OrderByDescending(kv => kv.Key).First();
            if (average > 0 && latest.Value > 2m * average)
            {
                alerts.Add(new FinOpsAlertDto("warn", "DailyCostSpike",
                    $"Cost on {latest.Key:yyyy-MM-dd} ({latest.Value:C2}) is above 2× the period daily average ({average:C2})",
                    atUtc));
            }
        }

        return alerts;
    }

    public async Task<RunTelemetryDto?> GetRunTelemetryAsync(string runId, CancellationToken cancellationToken = default)
    {
        var rows = await _metrics.Query
            .Where(m => m.RunId == runId)
            .OrderBy(m => m.RecordedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // AgentRun ids são gravados como Guid "N" (sem hífens) — aceita o
        // formato com hífens também.
        if (rows.Count == 0 && Guid.TryParse(runId, out var guid))
        {
            var normalized = guid.ToString("N");
            rows = await _metrics.Query
                .Where(m => m.RunId == normalized)
                .OrderBy(m => m.RecordedAtUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (rows.Count > 0)
            {
                runId = normalized;
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var first = rows[0].RecordedAtUtc;
        var last = rows[^1].RecordedAtUtc;
        var duration = last > first ? (last - first).TotalSeconds : (double?)null;

        return new RunTelemetryDto(
            RunId: runId,
            TotalTokens: rows.Sum(r => r.TotalTokens),
            InputTokens: rows.Sum(r => r.InputTokens),
            OutputTokens: rows.Sum(r => r.OutputTokens),
            CacheTokens: rows.Sum(r => r.CacheWriteTokens + r.CacheReadTokens),
            CostUsd: rows.Sum(r => r.CostUsd),
            DurationSeconds: duration,
            BudgetCapUsd: rows.Select(r => r.BudgetCapUsd).LastOrDefault(c => c is not null),
            Metrics: rows.Select(ToDto).ToList());
    }

    private async Task<IReadOnlyList<ModelPriceRateInfo>> LoadRatesAsync(CancellationToken cancellationToken)
    {
        var rows = await _rates.Query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows
            .Select(r => new ModelPriceRateInfo(r.Provider, r.ModelPattern, r.InputPer1M, r.OutputPer1M, r.CacheWritePer1M, r.CacheReadPer1M))
            .ToList();
    }

    private static RunCostMetricDto ToDto(RunCostMetric m) =>
        new(
            m.RunId,
            m.StageKey,
            m.AgentType.ToString(),
            m.ModelName,
            m.InputTokens,
            m.OutputTokens,
            m.CacheWriteTokens + m.CacheReadTokens,
            m.CostUsd,
            new DateTimeOffset(m.RecordedAtUtc, TimeSpan.Zero));
}
