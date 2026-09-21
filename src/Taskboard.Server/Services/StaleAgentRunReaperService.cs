using Taskboard.Agents;
using Taskboard.Application.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// Reconciles AgentRun rows stuck in Queued/Running with no live job —
/// orphans of a server restart or dead process (SPEC-20260920-harness-maintenance-jobs
/// RF-001). Every 5min; threshold configurable via
/// <c>Taskboard:RecurringJobs:StaleRunThresholdMinutes</c> (default 15).
/// </summary>
public sealed class StaleAgentRunReaperService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentOrchestrationService _orchestrator;
    private readonly ILogger<StaleAgentRunReaperService> _logger;
    private readonly TimeSpan _threshold;

    public StaleAgentRunReaperService(
        IServiceScopeFactory scopeFactory,
        IAgentOrchestrationService orchestrator,
        IConfiguration configuration,
        ILogger<StaleAgentRunReaperService> logger)
    {
        _scopeFactory = scopeFactory;
        _orchestrator = orchestrator;
        _logger = logger;
        var minutes = configuration.GetValue("Taskboard:RecurringJobs:StaleRunThresholdMinutes", 15);
        _threshold = TimeSpan.FromMinutes(Math.Max(1, minutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Stale run reaper started (interval {Interval}, threshold {Threshold}).", Interval, _threshold);

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
            var reaper = scope.ServiceProvider.GetRequiredService<StaleRunReaper>();
            var reaped = await reaper.RunOnceAsync(
                _orchestrator.GetLiveRunIds(), _threshold, stoppingToken).ConfigureAwait(false);
            foreach (var runId in reaped)
            {
                _logger.LogWarning("Agent run {RunId} finished as Failed — stale (no live job).", runId);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stale run reaper tick failed.");
        }
    }
}
