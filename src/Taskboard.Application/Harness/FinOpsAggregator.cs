using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.CliMetrics;
using Taskboard.Domain.Entities.CliMetrics;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Harness;
using Taskboard.Repositories;

namespace Taskboard.Application.Harness;

/// <summary>
/// Projects USD cost onto ingested CLI session metrics
/// (SPEC-20260920-harness-recurring-jobs RF-001). One idempotent pass costs
/// every session with <c>CostUsd == null</c> (re-ingested rows reset to null so
/// new token counts re-cost) and folds the delta into the (Kind, Day) usage
/// aggregates. Called every 30s by <c>FinOpsAggregationService</c>.
/// </summary>
public sealed class FinOpsAggregator
{
    internal const int BatchSize = 500;

    private readonly IRepository<CliSessionMetric> _sessions;
    private readonly IRepository<CliDailyUsageAggregate> _aggregates;
    private readonly IRepository<ModelPriceRate> _rates;

    public FinOpsAggregator(
        IRepository<CliSessionMetric> sessions,
        IRepository<CliDailyUsageAggregate> aggregates,
        IRepository<ModelPriceRate> rates)
    {
        _sessions = sessions;
        _aggregates = aggregates;
        _rates = rates;
    }

    /// <summary>Costs up to <see cref="BatchSize"/> un-costed sessions. Returns the count processed.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _sessions.Query
            .Where(s => s.CostUsd == null)
            .OrderBy(s => s.StartedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sessions.Count == 0)
        {
            return 0;
        }

        var rates = await _rates.Query
            .Select(r => new ModelPriceRateInfo(r.Provider, r.ModelPattern, r.InputPer1M, r.OutputPer1M, r.CacheWritePer1M, r.CacheReadPer1M))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = DateTime.UtcNow;
        foreach (var session in sessions)
        {
            var (_, model) = CliModelName.Normalize(session.ModelName);
            // TokensCached is the closest CLI-level signal to cache-read usage.
            var usage = new TokenUsage(
                session.TokensInput ?? 0,
                session.TokensOutput ?? 0,
                0,
                session.TokensCached ?? 0);
            session.SetCost(usage.TotalTokens == 0 ? 0m : TokenCostCalculator.Calculate(rates, model, usage));
        }

        // Persist session costs first — the bucket recompute reads them from
        // the database (a Sum over tracked-but-unsaved rows would see NULLs).
        await _sessions.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Recompute the affected (Kind, Day) buckets in full — delta-summing
        // would double-count when re-ingested sessions get re-costed.
        var affectedDays = sessions
            .Select(s => (s.Kind, Day: s.StartedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .Distinct()
            .ToList();

        foreach (var (kind, day) in affectedDays)
        {
            var dayStart = DateTime.SpecifyKind(
                DateTime.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTimeKind.Utc);
            var dayEnd = dayStart.AddDays(1);
            var total = await _sessions.Query
                .Where(s => s.Kind == kind && s.CostUsd != null
                    && s.StartedAtUtc >= dayStart && s.StartedAtUtc < dayEnd)
                .SumAsync(s => s.CostUsd!.Value, cancellationToken)
                .ConfigureAwait(false);

            var aggregate = await _aggregates.Query
                .FirstOrDefaultAsync(a => a.Kind == kind && a.Day == day, cancellationToken)
                .ConfigureAwait(false);
            if (aggregate is null)
            {
                aggregate = CliDailyUsageAggregate.Register(kind, day, now);
                aggregate.SetCost(total, now);
                await _aggregates.AddAsync(aggregate, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                aggregate.SetCost(total, now);
            }
        }

        await _aggregates.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return sessions.Count;
    }
}
