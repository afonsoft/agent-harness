using Taskboard.Application.Contracts.Skills;

namespace Taskboard.Server.Services;

/// <summary>
/// Skills synchronization as managed run-once job <c>skills-sync</c>
/// (SPEC-20260929-jobs-dashboard): runs at startup when enabled and on manual
/// trigger from /jobs; never blocks or fails application startup.
/// SPEC-20260915-skills-repo-sync RF-006.
/// </summary>
public sealed class SkillsSyncHostedService : ManagedJobService
{
    public const string JobKey = "skills-sync";

    private readonly ISkillsSyncService _syncService;
    private readonly IServiceScopeFactory _scopeFactory;

    public SkillsSyncHostedService(
        ISkillsSyncService syncService,
        IServiceScopeFactory scopeFactory,
        JobRegistry registry,
        ILogger<SkillsSyncHostedService> logger)
        : base(registry, JobKey, logger)
    {
        _syncService = syncService;
        _scopeFactory = scopeFactory;
    }

    protected override async Task<string?> RunJobAsync(CancellationToken cancellationToken)
    {
        var agents = await EnabledAgentResolver
            .ResolveAsync(_scopeFactory, cancellationToken)
            .ConfigureAwait(false);
        await _syncService.SyncAsync(agents, cancellationToken).ConfigureAwait(false);
        return $"skills synced for {agents.Count} agent(s)";
    }
}
