using Taskboard.Application.Contracts.Skills;

namespace Taskboard.Server.Services;

/// <summary>
/// Triggers a skills synchronization in the background when the application
/// starts (SPEC-20260915-skills-repo-sync RF-006). Never blocks or fails the
/// application startup.
/// </summary>
public sealed class SkillsSyncHostedService : BackgroundService
{
    private readonly ISkillsSyncService _syncService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SkillsSyncHostedService> _logger;

    public SkillsSyncHostedService(
        ISkillsSyncService syncService,
        IServiceScopeFactory scopeFactory,
        ILogger<SkillsSyncHostedService> logger)
    {
        _syncService = syncService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var agents = await EnabledAgentResolver
                    .ResolveAsync(_scopeFactory, stoppingToken)
                    .ConfigureAwait(false);
                await _syncService.SyncAsync(agents, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Startup skills sync failed.");
            }
        }, stoppingToken);

        return Task.CompletedTask;
    }
}
