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
            // SPEC-20260922 RF-007: CLI sessions whose model only matches the
            // "*" wildcard have no real price coverage — fall back to the flat
            // $9.5/1M rate; the cost is flagged as estimated downstream.
            var cost = usage.TotalTokens == 0
                ? 0m
                : TokenCostCalculator.ResolveSpecific(rates, model) is not null
                    ? TokenCostCalculator.Calculate(rates, model, usage)
                    : usage.TotalTokens * FinOpsPricing.FallbackUsdPerMTok / 1_000_000m;
            session.SetCost(cost);
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
            var dayRows = await _sessions.Query
                .Where(s => s.Kind == kind
                    && s.StartedAtUtc >= dayStart && s.StartedAtUtc < dayEnd)
                .Select(s => new { s.CostUsd, s.TokensEstimated })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var total = dayRows.Where(r => r.CostUsd != null).Sum(r => r.CostUsd!.Value);
            var allEstimated = dayRows.Count > 0 && dayRows.All(r => r.TokensEstimated);

            var aggregate = await _aggregates.Query
                .FirstOrDefaultAsync(a => a.Kind == kind && a.Day == day, cancellationToken)
                .ConfigureAwait(false);
            if (aggregate is null)
            {
                aggregate = CliDailyUsageAggregate.Register(kind, day, now);
                aggregate.SetCost(total, now);
                aggregate.SetTokensEstimated(allEstimated, now);
                await _aggregates.AddAsync(aggregate, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                aggregate.SetCost(total, now);
                aggregate.SetTokensEstimated(allEstimated, now);
            }
        }

        await _aggregates.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return sessions.Count;
    }
}
