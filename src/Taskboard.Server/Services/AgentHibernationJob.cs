using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261020-agent-hibernation RF-002: sweeps the attention model every
/// 60s and hibernates done/idle sessions past the threshold (default 30min,
/// clamped 5–1440 via <c>Taskboard:AgentHibernation:IdleThresholdMinutes</c>).
/// Failures log and retry next tick — the sweep must never die.
/// </summary>
public sealed class AgentHibernationJob : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IAgentHibernationService _hibernation;
    private readonly ILogger<AgentHibernationJob> _logger;
    private readonly TimeSpan _idleThreshold;

    public AgentHibernationJob(
        IAgentHibernationService hibernation,
        IConfiguration configuration,
        ILogger<AgentHibernationJob> logger)
    {
        _hibernation = hibernation;
        _logger = logger;
        var minutes = Math.Clamp(
            configuration.GetValue("Taskboard:AgentHibernation:IdleThresholdMinutes", 30),
            5, 1440);
        _idleThreshold = TimeSpan.FromMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var count = await _hibernation
                    .ScanOnceAsync("local", _idleThreshold, stoppingToken)
                    .ConfigureAwait(false);
                if (count > 0)
                {
                    _logger.LogInformation("Hibernated {Count} idle session(s).", count);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hibernation sweep tick failed.");
            }
        }
    }
}
