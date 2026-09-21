using Taskboard.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// Periodic retention for <c>AgentRunEvent</c> rows
/// (SPEC-20260921-agent-execution-event-pipeline): deletes events older than
/// <c>Taskboard:AgentEvents:RetentionDays</c> (default 30, 0 disables).
/// Runs once at startup and then every 6 hours.
/// </summary>
public sealed class AgentRunEventRetentionService : BackgroundService
{
    public const string RetentionDaysKey = "Taskboard:AgentEvents:RetentionDays";
    private const int DefaultRetentionDays = 30;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentRunEventRetentionService> _logger;

    public AgentRunEventRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<AgentRunEventRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var days = _configuration.GetValue(RetentionDaysKey, DefaultRetentionDays);
        if (days <= 0)
        {
            return;
        }

        await PurgeAsync(days, stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await PurgeAsync(days, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PurgeAsync(int retentionDays, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAgentRunEventRepository>();
            var purged = await repository.DeleteOlderThanAsync(
                DateTimeOffset.UtcNow.AddDays(-retentionDays), ct).ConfigureAwait(false);
            if (purged > 0)
            {
                _logger.LogInformation(
                    "Agent run events: purged {Count} rows past {Days}d retention", purged, retentionDays);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent run events retention purge failed; will retry at the next interval.");
        }
    }
}
