using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261020-agents-feed-notifications RF-009: daily prune of feed items
/// older than the retention window (default 30d). Interval/window via
/// <c>Taskboard:AgentFeed:Prune*</c>; failures log and retry next tick.
/// </summary>
public sealed class AgentFeedPruneService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AgentFeedPruneService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _retention;

    public AgentFeedPruneService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AgentFeedPruneService> logger)
    {
        _services = services;
        _logger = logger;
        _interval = TimeSpan.FromHours(
            Math.Max(1, configuration.GetValue("Taskboard:AgentFeed:PruneIntervalHours", 24)));
        _retention = TimeSpan.FromDays(
            Math.Max(1, configuration.GetValue("Taskboard:AgentFeed:RetentionDays", 30)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var items = scope.ServiceProvider.GetRequiredService<IAgentFeedRepository>();
                var pruned = await items
                    .DeleteOlderThanAsync(DateTime.UtcNow - _retention, stoppingToken)
                    .ConfigureAwait(false);
                if (pruned > 0)
                {
                    _logger.LogInformation("agent feed prune removed {Count} expired item(s)", pruned);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "agent feed prune tick failed");
            }
        }
    }
}
