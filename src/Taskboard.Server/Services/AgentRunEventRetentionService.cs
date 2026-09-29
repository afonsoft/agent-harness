using Taskboard.Agents;

namespace Taskboard.Server.Services;

/// <summary>
/// Periodic retention for <c>AgentRunEvent</c> rows
/// (SPEC-20260921-agent-execution-event-pipeline): deletes events older than
/// <c>Taskboard:AgentEvents:RetentionDays</c> (default 30, 0 disables).
/// Managed job <c>agent-run-event-retention</c> (SPEC-20260929-jobs-dashboard),
/// default every 6 hours.
/// </summary>
public sealed class AgentRunEventRetentionService : ManagedJobService
{
    public const string JobKey = "agent-run-event-retention";
    public const string RetentionDaysKey = "Taskboard:AgentEvents:RetentionDays";
    private const int DefaultRetentionDays = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentRunEventRetentionService> _logger;

    public AgentRunEventRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        JobRegistry registry,
        ILogger<AgentRunEventRetentionService> logger)
        : base(registry, JobKey, logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task<string?> RunJobAsync(CancellationToken cancellationToken)
    {
        var days = _configuration.GetValue(RetentionDaysKey, DefaultRetentionDays);
        if (days <= 0)
        {
            return "retention disabled (RetentionDays <= 0)";
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentRunEventRepository>();
        var purged = await repository.DeleteOlderThanAsync(
            DateTimeOffset.UtcNow.AddDays(-days), cancellationToken).ConfigureAwait(false);
        if (purged > 0)
        {
            _logger.LogInformation(
                "Agent run events: purged {Count} rows past {Days}d retention", purged, days);
        }

        return purged > 0 ? $"purged {purged} rows past {days}d" : null;
    }
}
