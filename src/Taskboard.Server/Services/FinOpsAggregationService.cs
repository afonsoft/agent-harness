using Taskboard.Application.Harness;

namespace Taskboard.Server.Services;

/// <summary>
/// Recurring FinOps projection (SPEC-20260920-harness-recurring-jobs RF-001):
/// costs newly ingested CLI session metrics — managed job
/// <c>finops-aggregation</c> (SPEC-20260929-jobs-dashboard), default 30s.
/// </summary>
public sealed class FinOpsAggregationService : ManagedJobService
{
    public const string JobKey = "finops-aggregation";

    private readonly IServiceScopeFactory _scopeFactory;

    public FinOpsAggregationService(
        IServiceScopeFactory scopeFactory,
        JobRegistry registry,
        ILogger<FinOpsAggregationService> logger)
        : base(registry, JobKey, logger)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task<string?> RunJobAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var aggregator = scope.ServiceProvider.GetRequiredService<FinOpsAggregator>();
        var processed = await aggregator.RunOnceAsync(cancellationToken).ConfigureAwait(false);
        return processed > 0 ? $"{processed} sessions costed" : null;
    }
}
