using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261020-agent-hibernation RF-002/RF-010: sweeps the attention model
/// every 60s and hibernates done/idle sessions past the effective threshold —
/// per-user pref (5–1440min, default 30) with the env-level override
/// <c>Taskboard:AgentHibernation:IdleThresholdMinutes</c> winning when set.
/// Failures log and retry next tick — the sweep must never die.
/// </summary>
public sealed class AgentHibernationJob : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IAgentHibernationService _hibernation;
    private readonly ILogger<AgentHibernationJob> _logger;
    private readonly TimeSpan? _configOverride;

    public AgentHibernationJob(
        IAgentHibernationService hibernation,
        IConfiguration configuration,
        ILogger<AgentHibernationJob> logger)
    {
        _hibernation = hibernation;
        _logger = logger;
        _configOverride = configuration["Taskboard:AgentHibernation:IdleThresholdMinutes"]
            is { Length: > 0 } raw && int.TryParse(raw, out var minutes)
                ? TimeSpan.FromMinutes(Math.Clamp(minutes, 5, 1440))
                : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                // RF-010: pref changes apply on the next tick — read every pass.
                var prefs = await _hibernation
                    .GetPrefsAsync("local", stoppingToken).ConfigureAwait(false);
                var threshold = _configOverride
                    ?? TimeSpan.FromMinutes(prefs.IdleThresholdMinutes);

                var count = await _hibernation
                    .ScanOnceAsync("local", threshold, stoppingToken)
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
