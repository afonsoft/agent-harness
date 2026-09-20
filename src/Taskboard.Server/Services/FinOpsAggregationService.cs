using Taskboard.Application.Harness;

namespace Taskboard.Server.Services;

/// <summary>
/// Recurring FinOps projection (SPEC-20260920-harness-recurring-jobs RF-001):
/// costs newly ingested CLI session metrics every 30 seconds so the /finops
/// dashboard reflects real usage without waiting for Harness runs.
/// </summary>
public sealed class FinOpsAggregationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FinOpsAggregationService> _logger;

    public FinOpsAggregationService(
        IServiceScopeFactory scopeFactory,
        ILogger<FinOpsAggregationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FinOps aggregation started (interval {Interval}).", Interval);

        // Initial pass so existing uncosted sessions are projected on boot.
        await SafeRunAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await SafeRunAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task SafeRunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var aggregator = scope.ServiceProvider.GetRequiredService<FinOpsAggregator>();
            var processed = await aggregator.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            if (processed > 0)
            {
                _logger.LogDebug("FinOps aggregation: {Count} sessions costed.", processed);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FinOps aggregation tick failed.");
        }
    }
}
