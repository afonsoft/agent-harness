using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Harness;
using Taskboard.Harness.FinOps;
using Taskboard.Repositories;

namespace Taskboard.Application.Harness;

/// <summary>
/// FinOps control plane (SPEC-20260919-ade-observability-finops): records token
/// usage per run/stage, computes exact decimal costs from the seeded
/// <see cref="ModelPriceRate"/> table and aggregates the dashboard summaries.
/// </summary>
public sealed class FinOpsService : IFinOpsService
{
    private readonly IRepository<RunCostMetric> _metrics;
    private readonly IRepository<ModelPriceRate> _rates;

    public FinOpsService(IRepository<RunCostMetric> metrics, IRepository<ModelPriceRate> rates)
    {
        _metrics = metrics;
        _rates = rates;
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
        var since = period switch
        {
            "last-7-days" => DateTime.UtcNow.AddDays(-7),
            "all" or null or "" => DateTime.MinValue,
            _ => DateTime.UtcNow.AddDays(-30) // default: last-30-days
        };

        var rows = await _metrics.Query
            .Where(m => m.RecordedAtUtc >= since)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new FinOpsSummaryDto(
            TotalCostUsd: rows.Sum(r => r.CostUsd),
            TotalTokens: rows.Sum(r => r.TotalTokens),
            RunsCount: rows.Select(r => r.RunId).Distinct().Count(),
            CostByAgent: rows
                .GroupBy(r => r.AgentType.ToString())
                .ToDictionary(g => g.Key, g => g.Sum(r => r.CostUsd)),
            CostByModel: rows
                .Where(r => r.ModelName is not null)
                .GroupBy(r => r.ModelName!)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.CostUsd)),
            DailyCosts: rows
                .GroupBy(r => DateOnly.FromDateTime(r.RecordedAtUtc))
                .OrderBy(g => g.Key)
                .Select(g => new FinOpsDailyCostDto(g.Key, g.Sum(r => r.CostUsd), g.Sum(r => r.TotalTokens)))
                .ToList());
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
