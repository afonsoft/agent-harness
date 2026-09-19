using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.GitHub;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Orquestra a execução de agentes CLI em background usando Channel e SignalR.
/// </summary>
public sealed class AgentOrchestrationService : BackgroundService, IAgentOrchestrationService
{
    private readonly IAgentAcpClient _acpClient;
    private readonly IAgentDiscoveryService _discoveryService;
    private readonly IAgentLogBroadcaster _logBroadcaster;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IGitHubService _gitHubService;
    private readonly Channel<QueuedJob> _channel = Channel.CreateUnbounded<QueuedJob>();
    private readonly ConcurrentDictionary<string, RunningJob> _running = new();
    private readonly ConcurrentDictionary<string, List<AgentLogMessage>> _logs = new();

    public AgentOrchestrationService(
        IAgentAcpClient acpClient,
        IAgentDiscoveryService discoveryService,
        IAgentLogBroadcaster logBroadcaster,
        IServiceScopeFactory serviceScopeFactory,
        IGitHubService gitHubService)
    {
        _acpClient = acpClient;
        _discoveryService = discoveryService;
        _logBroadcaster = logBroadcaster;
        _serviceScopeFactory = serviceScopeFactory;
        _gitHubService = gitHubService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            _ = Task.Run(async () => await RunAsync(job, stoppingToken), stoppingToken);
        }
    }

    public async Task<IReadOnlyList<AgentInfo>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default)
    {
        var discovered = await _discoveryService.DiscoverAsync(cancellationToken);
        var eligible = await GetEligibleTypesAsync(cancellationToken);
        var busyTypes = _running.Values
            .Select(r => r.Request.AgentType)
            .ToHashSet();

        return discovered
            .Where(a => a.Status == AgentStatus.Available && eligible.Contains(a.Type))
            .Select(a => a with
            {
                Status = busyTypes.Contains(a.Type) ? AgentStatus.Busy : a.Status
            })
            .ToList()
            .AsReadOnly();
    }

    public async Task<bool> EnqueueAsync(AgentExecutionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureLogList(request.IssueId);

        // SPEC-20260917-agent-eligibility-task-badge RF-003: never queue an agent
        // that is disabled or whose CLI is not authenticated right now.
        var eligible = await GetEligibleTypesAsync(cancellationToken);
        if (!eligible.Contains(request.AgentType))
        {
            AppendLog(request.IssueId, new AgentLogMessage(
                DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                $"Agent {request.AgentType} rejected: disabled or CLI not authenticated."));
            return false;
        }

        // SPEC-20260918-agent-model-config RF-006: resolve the effective model
        // (override ?? curated) once — the run record and the argv must agree.
        var resolvedModel = await ResolveModelAsync(request, cancellationToken);
        request = request with { ResolvedModelName = resolvedModel };

        var runId = await TryCreateRunAsync(request, cancellationToken);
        AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Queued {request.AgentType} for issue {request.IssueId}."));
        _channel.Writer.TryWrite(new QueuedJob(request, runId));
        return true;
    }

    public async Task<IReadOnlyList<AgentLogMessage>> GetLogsAsync(string issueId, CancellationToken cancellationToken = default)
    {
        var list = _logs.GetValueOrDefault(issueId);
        if (list is not null && list.Count > 0)
        {
            lock (list)
            {
                return list.ToList().AsReadOnly();
            }
        }

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentLogRepository>();
        return await repository.GetByIssueIdAsync(issueId, cancellationToken);
    }

    public async Task ClearLogsAsync(string issueId, CancellationToken cancellationToken = default)
    {
        _logs.TryRemove(issueId, out _);

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentLogRepository>();
        await repository.DeleteByIssueIdAsync(issueId, cancellationToken);
    }

    public Task CancelAsync(string issueId, CancellationToken cancellationToken = default)
    {
        if (_running.TryGetValue(issueId, out var job))
        {
            job.CancellationTokenSource.Cancel();
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AgentRunDto>> GetRunsAsync(string issueId, int take = 5, CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        return await repository.GetByIssueIdAsync(issueId, take, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentRunDto>> GetLatestRunsAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
        return await repository.GetLatestPerIssueAsync(cancellationToken);
    }

    private async Task RunAsync(QueuedJob job, CancellationToken stoppingToken)
    {
        var request = job.Request;
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var runningJob = new RunningJob(request, cancellationTokenSource);
        _running[request.IssueId] = runningJob;
        EnsureLogList(request.IssueId);

        // SPEC-20260919-harness-workspace-isolation T4: run the agent inside a
        // dedicated git worktree when RepoPath points at a git repository.
        var worktreeRunId = job.RunId?.ToString("N") ?? Guid.NewGuid().ToString("N");
        var isolation = await TryIsolateAsync(request, worktreeRunId, stoppingToken);
        if (isolation is not null)
        {
            request = request with { RepoPath = isolation.Path, Branch = isolation.Branch };
        }

        AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Starting {request.AgentType} on {request.RepoPath}..."));
        await UpdateRunAsync(job.RunId, (repo, id) => repo.MarkRunningAsync(id, CancellationToken.None), stoppingToken);

        var progress = new Progress<AgentLogMessage>(async message =>
        {
            AppendLog(message.IssueId, message);
            await _logBroadcaster.BroadcastAsync(message);
        });

        try
        {
            var result = await _acpClient.ExecuteAsync(request, progress, cancellationTokenSource.Token);

            AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Agent finished with exit code {result.ExitCode}."));
            await UpdateRunAsync(job.RunId, (repo, id) => repo.FinishAsync(id, AgentRunState.Succeeded, CancellationToken.None), stoppingToken);

            if (result.IsSuccess)
            {
                await MarkWorktreeAsync(worktreeRunId, completed: true, stoppingToken);
                await MoveToReviewAsync(request);
            }
            else
            {
                await MarkWorktreeAsync(worktreeRunId, completed: false, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, "Agent execution was cancelled."));
            await UpdateRunAsync(job.RunId, (repo, id) => repo.FinishAsync(id, AgentRunState.Canceled, CancellationToken.None), stoppingToken);
            await MarkWorktreeAsync(worktreeRunId, completed: false, stoppingToken);
        }
        catch (Exception ex)
        {
            AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Agent error: {ex.Message}"));
            await UpdateRunAsync(job.RunId, (repo, id) => repo.FinishAsync(id, AgentRunState.Failed, CancellationToken.None), stoppingToken);
            await MarkWorktreeAsync(worktreeRunId, completed: false, stoppingToken);
        }
        finally
        {
            _running.TryRemove(request.IssueId, out _);
            cancellationTokenSource.Dispose();
        }
    }

    /// <summary>
    /// Creates an isolated worktree for the run when <see cref="AgentExecutionRequest.RepoPath"/>
    /// points at a git repository. Falls back to running directly on RepoPath when
    /// isolation is unavailable — tracking must never break orchestration.
    /// </summary>
    private async Task<WorktreeSessionDto?> TryIsolateAsync(
        AgentExecutionRequest request,
        string worktreeRunId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RepoPath))
        {
            return null;
        }

        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var isolation = scope.ServiceProvider.GetService<IWorkspaceIsolationService>();
            if (isolation is null)
            {
                return null;
            }

            var taskSlug = request.Scope ?? $"issue-{request.IssueNumber}";
            var session = await isolation.CreateWorktreeAsync(
                worktreeRunId,
                request.RepoPath,
                baseBranch: request.Branch ?? "HEAD",
                taskSlug,
                retainOnFailure: true,
                cancellationToken);

            AppendLog(request.IssueId, new AgentLogMessage(
                DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                $"Worktree isolated at {session.Path} (branch {session.Branch})."));
            return session;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppendLog(request.IssueId, new AgentLogMessage(
                DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                $"Worktree isolation failed ({ex.Message}); running directly on the repository path."));
            return null;
        }
    }

    /// <summary>
    /// Marks the worktree session completed (kept for diff review) or failed
    /// (retained for inspection per <c>RetainOnFailure</c>). Best-effort.
    /// </summary>
    private async Task MarkWorktreeAsync(string worktreeRunId, bool completed, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var isolation = scope.ServiceProvider.GetService<IWorkspaceIsolationService>();
            if (isolation is null)
            {
                return;
            }

            if (completed)
            {
                await isolation.MarkCompletedAsync(worktreeRunId, CancellationToken.None);
            }
            else
            {
                await isolation.MarkFailedAsync(worktreeRunId, CancellationToken.None);
            }
        }
        catch
        {
            // Worktree bookkeeping is best-effort; orchestration outcome is unaffected.
        }
    }

    private async Task MoveToReviewAsync(AgentExecutionRequest request)
    {
        try
        {
            await _gitHubService.UpdateIssueColumnAsync(
                request.RepositoryFullName,
                request.IssueNumber,
                GitHubBoardColumn.InProgress,
                GitHubBoardColumn.InReview);
        }
        catch (Exception ex)
        {
            AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Failed to move issue to review: {ex.Message}"));
        }
    }

    /// <summary>
    /// AgentTypes eligible for execution right now: installed + authenticated CLI
    /// AND enabled in Settings — resolved via the scoped
    /// <see cref="IAgentEligibilityService"/> (Integrations has no Domain access).
    /// </summary>
    private async Task<IReadOnlySet<AgentType>> GetEligibleTypesAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var eligibility = scope.ServiceProvider.GetRequiredService<IAgentEligibilityService>();
        return await eligibility.GetEligibleTypesAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the effective model name through the scoped
    /// <see cref="IAgentModelConfigService"/> (override ?? curated). Failures
    /// degrade to the curated table — model resolution never blocks a run.
    /// </summary>
    private async Task<string?> ResolveModelAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var config = scope.ServiceProvider.GetRequiredService<IAgentModelConfigService>();
            return await config.ResolveModelAsync(request.AgentType, request.ModelTier, cancellationToken);
        }
        catch (Exception ex)
        {
            AppendLog(request.IssueId, new AgentLogMessage(
                DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                $"Model config lookup failed ({ex.Message}); using curated default."));
            return null;
        }
    }

    private async Task<Guid?> TryCreateRunAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
            var run = await repository.EnqueueAsync(
                request.IssueId,
                request.AgentType,
                request.ModelTier,
                request.ResolvedModelName ?? AgentCliModels.ModelFor(request.AgentType, request.ModelTier),
                cancellationToken);
            return run.Id;
        }
        catch (Exception ex)
        {
            // Run tracking must never break orchestration.
            AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Failed to record agent run: {ex.Message}"));
            return null;
        }
    }

    private async Task UpdateRunAsync(Guid? runId, Func<IAgentRunRepository, Guid, Task> update, CancellationToken cancellationToken)
    {
        if (runId is null)
        {
            return;
        }

        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAgentRunRepository>();
            await update(repository, runId.Value);
        }
        catch
        {
            // Run tracking is best-effort; orchestration outcome is unaffected.
        }
    }

    private void EnsureLogList(string issueId)
    {
        _logs.GetOrAdd(issueId, _ => []);
    }

    private void AppendLog(string issueId, AgentLogMessage message)
    {
        var list = _logs.GetOrAdd(issueId, _ => []);
        lock (list)
        {
            list.Add(message);
        }

        _ = _logBroadcaster.BroadcastAsync(message);
        _ = Task.Run(async () =>
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAgentLogRepository>();
            await repository.AppendAsync(message);
        });
    }

    private sealed record QueuedJob(AgentExecutionRequest Request, Guid? RunId);

    private sealed class RunningJob
    {
        public RunningJob(AgentExecutionRequest request, CancellationTokenSource cancellationTokenSource)
        {
            Request = request;
            CancellationTokenSource = cancellationTokenSource;
        }

        public AgentExecutionRequest Request { get; }
        public CancellationTokenSource CancellationTokenSource { get; }
    }
}
