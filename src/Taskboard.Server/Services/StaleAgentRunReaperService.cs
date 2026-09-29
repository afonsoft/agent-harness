using Taskboard.Agents;
using Taskboard.Application.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// Reconciles AgentRun rows stuck in Queued/Running with no live job —
/// orphans of a server restart or dead process (SPEC-20260920-harness-maintenance-jobs
/// RF-001). Managed job <c>stale-run-reaper</c> (SPEC-20260929-jobs-dashboard),
/// default every 5min; threshold configurable via
/// <c>Taskboard:RecurringJobs:StaleRunThresholdMinutes</c> (default 15).
/// </summary>
public sealed class StaleAgentRunReaperService : ManagedJobService
{
    public const string JobKey = "stale-run-reaper";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentOrchestrationService _orchestrator;
    private readonly TimeSpan _threshold;

    public StaleAgentRunReaperService(
        IServiceScopeFactory scopeFactory,
        IAgentOrchestrationService orchestrator,
        IConfiguration configuration,
        JobRegistry registry,
        ILogger<StaleAgentRunReaperService> logger)
        : base(registry, JobKey, logger)
    {
        _scopeFactory = scopeFactory;
        _orchestrator = orchestrator;
        var minutes = configuration.GetValue("Taskboard:RecurringJobs:StaleRunThresholdMinutes", 15);
        _threshold = TimeSpan.FromMinutes(Math.Max(1, minutes));
    }

    protected override async Task<string?> RunJobAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var reaper = scope.ServiceProvider.GetRequiredService<StaleRunReaper>();
        var reaped = await reaper.RunOnceAsync(
            _orchestrator.GetLiveRunIds(), _threshold, cancellationToken).ConfigureAwait(false);
        return reaped.Count > 0 ? $"{reaped.Count} stale run(s) failed" : null;
    }
}
