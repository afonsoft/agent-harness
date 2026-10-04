using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.GitHub;
using Taskboard.Harness;
using Taskboard.Harness.FinOps;

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
    private readonly IAgentExecutionEventSink? _eventSink;
    private readonly Channel<QueuedJob> _channel = Channel.CreateUnbounded<QueuedJob>();
    private readonly ConcurrentDictionary<string, RunningJob> _running = new();
    private readonly ConcurrentDictionary<Guid, byte> _liveRunIds = new();
    private readonly ConcurrentDictionary<string, List<AgentLogMessage>> _logs = new();

    public AgentOrchestrationService(
        IAgentAcpClient acpClient,
        IAgentDiscoveryService discoveryService,
        IAgentLogBroadcaster logBroadcaster,
        IServiceScopeFactory serviceScopeFactory,
        IGitHubService gitHubService,
        IAgentExecutionEventSink? eventSink = null)
    {
        _acpClient = acpClient;
        _discoveryService = discoveryService;
        _logBroadcaster = logBroadcaster;
        _serviceScopeFactory = serviceScopeFactory;
        _gitHubService = gitHubService;
        _eventSink = eventSink;
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
        var busyByType = _running.Values
            .GroupBy(r => r.Request.AgentType)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.StartedAtUtc).First());
        var now = DateTimeOffset.UtcNow;

        return discovered
            .Where(a => a.Status == AgentStatus.Available && eligible.Contains(a.Type))
            .Select(a => busyByType.TryGetValue(a.Type, out var busy)
                ? a with
                {
                    Status = AgentStatus.Busy,
                    ActiveIssueId = busy.Request.IssueId,
                    ActiveElapsedSeconds = Math.Max(0, (now - busy.StartedAtUtc).TotalSeconds),
                }
                : a)
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
        if (runId is { } id)
        {
            _liveRunIds.TryAdd(id, 0);
        }

        AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Queued {request.AgentType} for issue {request.IssueId}."));
        _channel.Writer.TryWrite(new QueuedJob(request, runId));
        return true;
    }

    /// <summary>RunIds enfileirados/em execução neste processo (SPEC-20260920-harness-maintenance-jobs RF-002).</summary>
    public IReadOnlyCollection<Guid> GetLiveRunIds() => _liveRunIds.Keys.ToArray();

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

        // Clear also wipes the normalized event stream — the shared timeline
        // must not keep showing history the user asked to delete.
        var events = scope.ServiceProvider.GetRequiredService<IAgentRunEventRepository>();
        await events.DeleteByScopeAsync(AgentEventScope.Issue, issueId, cancellationToken);
    }

    public Task<bool> CancelAsync(string issueId, CancellationToken cancellationToken = default)
    {
        if (!_running.TryGetValue(issueId, out var job))
        {
            return Task.FromResult(false);
        }

        job.CancellationTokenSource.Cancel();
        return Task.FromResult(true);
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
        // When the caller already provisioned the session (delegated tasks:
        // ExistingWorktreeRunId), reuse it — creating a second worktree here
        // strands the agent's writes outside the tree compare/promote diff.
        var worktreeRunId = request.ExistingWorktreeRunId
            ?? job.RunId?.ToString("N") ?? Guid.NewGuid().ToString("N");
        var isolation = await TryIsolateAsync(request, worktreeRunId, stoppingToken);
        if (isolation is not null)
        {
            request = request with { RepoPath = isolation.Path, Branch = isolation.Branch };
        }

        AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Starting {request.AgentType} on {request.RepoPath}..."));
        await UpdateRunAsync(job.RunId, (repo, id) => repo.MarkRunningAsync(id, CancellationToken.None));

        // SPEC-20260919-ade-observability-finops RF-004: root span for the run.
        // Child spans (stages, verification) inherit this trace context.
        using var runActivity = HarnessTelemetrySource.StartRunSpan(
            worktreeRunId, request.AgentType, request.ResolvedModelName);

        // RF-001/RF-003: token usage streamed on stdout is tracked live so a
        // run that crosses its budget cap is cancelled mid-flight.
        var budget = new RunBudgetState();
        var progress = CreateProgressHandler(request, worktreeRunId, cancellationTokenSource, budget);

        try
        {
            var result = await _acpClient.ExecuteAsync(request, progress, cancellationTokenSource.Token);

            AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Agent finished with exit code {result.ExitCode}."));

            // RF-001/RF-002: persist the run's cost metric when usage was
            // reported; RF-003: post-run check catches CLIs that only emit
            // usage at the end.
            var usage = result.Usage ?? budget.LatestUsage;
            if (usage is not null)
            {
                await RecordUsageAndPostBudgetCheckAsync(request, worktreeRunId, usage, budget, runActivity);
            }

            if (budget.BudgetExceeded)
            {
                await UpdateRunAsync(job.RunId, (repo, id) => repo.FinishAsync(id, AgentRunState.BudgetExceeded, CancellationToken.None));
                await MarkWorktreeAsync(worktreeRunId, completed: false);
            }
            else
            {
                await CompleteRunAsync(job, request, isolation, progress, worktreeRunId, result, cancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // RF-003: cancelamento disparado pelo budget cap finaliza como
            // BudgetExceeded, não Canceled.
            var finalState = budget.BudgetExceeded ? AgentRunState.BudgetExceeded : AgentRunState.Canceled;
            AppendLog(request.IssueId, new AgentLogMessage(
                DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                budget.BudgetExceeded ? "Agent stopped: budget cap exceeded." : "Agent execution was cancelled."));
            await UpdateRunAsync(job.RunId, (repo, id) => repo.FinishAsync(id, finalState, CancellationToken.None));
            await MarkWorktreeAsync(worktreeRunId, completed: false);
        }
        catch (Exception ex)
        {
            AppendLog(request.IssueId, new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System, $"Agent error: {ex.Message}"));
            await UpdateRunAsync(job.RunId, (repo, id) => repo.FinishAsync(id, AgentRunState.Failed, CancellationToken.None));
            await MarkWorktreeAsync(worktreeRunId, completed: false);
        }
        finally
        {
            _running.TryRemove(request.IssueId, out _);
            if (job.RunId is { } finishedRunId)
            {
                _liveRunIds.TryRemove(finishedRunId, out _);
            }

            cancellationTokenSource.Dispose();
        }
    }

    /// <summary>Mutable budget-tracking state shared between the progress callback and the run loop.</summary>
    private sealed class RunBudgetState
    {
        public TokenUsage? LatestUsage;
        public bool BudgetExceeded;
    }

    private Progress<AgentLogMessage> CreateProgressHandler(
        AgentExecutionRequest request,
        string worktreeRunId,
        CancellationTokenSource cancellationTokenSource,
        RunBudgetState budget)
    {
        var broadcastToken = cancellationTokenSource.Token;
        return new Progress<AgentLogMessage>(async message =>
        {
            try
            {
                AppendLog(message.IssueId, message);
                if (message.Stream == AgentLogStream.StdOut
                    && TokenUsageParser.TryExtract(message.Content) is { } parsed)
                {
                    budget.LatestUsage = parsed;
                    if (!budget.BudgetExceeded && request.MaxBudgetUsd is { } cap)
                    {
                        var cumulative = await TryComputeCumulativeCostAsync(request, worktreeRunId, budget.LatestUsage);
                        if (cumulative > cap)
                        {
                            budget.BudgetExceeded = true;
                            AppendLog(request.IssueId, new AgentLogMessage(
                                DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                                $"Budget cap ${cap:F2} exceeded (${cumulative:F4} cumulative) — cancelling run."));
                            await cancellationTokenSource.CancelAsync();
                        }
                    }
                }

                await _logBroadcaster.BroadcastAsync(message, broadcastToken);
            }
            catch (ObjectDisposedException)
            {
                // The run finished and its CTS was disposed before a late
                // progress callback fired — an unhandled exception here would
                // crash the process (async void); there is nothing left to do.
            }
            catch (OperationCanceledException)
            {
                // B-03: budget-cancel or run teardown mid-callback is expected —
                // an unhandled exception in this async-void callback crashes the
                // process, so expected cancellation is swallowed the same way.
            }
        });
    }

    private async Task RecordUsageAndPostBudgetCheckAsync(
        AgentExecutionRequest request,
        string worktreeRunId,
        TokenUsage usage,
        RunBudgetState budget,
        Activity? runActivity)
    {
        var cost = await TryRecordUsageAsync(request, worktreeRunId, usage);
        HarnessTelemetrySource.RecordUsage(runActivity, usage, cost);
        if (!budget.BudgetExceeded && request.MaxBudgetUsd is { } postCap)
        {
            var cumulative = await TryComputeCumulativeCostAsync(request, worktreeRunId, TokenUsage.Zero);
            if (cumulative > postCap)
            {
                budget.BudgetExceeded = true;
                AppendLog(request.IssueId, new AgentLogMessage(
                    DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                    $"Budget cap ${postCap:F2} exceeded (${cumulative:F4}) at run end."));
            }
        }
    }

    private async Task CompleteRunAsync(
        QueuedJob job,
        AgentExecutionRequest request,
        WorktreeSessionDto? isolation,
        IProgress<AgentLogMessage> progress,
        string worktreeRunId,
        AgentExecutionResult result,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            await UpdateRunAsync(job.RunId, (repo, id) => repo.FinishAsync(id, AgentRunState.Failed, CancellationToken.None));
            await MarkWorktreeAsync(worktreeRunId, completed: false);
            return;
        }

        // SPEC-20260919-harness-verification-loop RF-004/RF-005: quando o
        // run opta por verificação, o agente é re-invocado com o feedback
        // até passar ou esgotar tentativas (→ EscalatedToHuman).
        var report = await RunVerificationLoopAsync(request, isolation, progress, worktreeRunId, cancellationToken);
        if (report is { IsSuccess: false })
        {
            AppendLog(request.IssueId, new AgentLogMessage(
                DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                $"Verification failed ({report.Status}) — not moving to review."));
            await UpdateRunAsync(job.RunId, (repo, id) => repo.FinishAsync(id, AgentRunState.Failed, CancellationToken.None));
            await MarkWorktreeAsync(worktreeRunId, completed: false);
        }
        else
        {
            await UpdateRunAsync(job.RunId, (repo, id) => repo.FinishAsync(id, AgentRunState.Succeeded, CancellationToken.None));
            await MarkWorktreeAsync(worktreeRunId, completed: true);
            await MoveToReviewAsync(request);
        }
    }

    /// <summary>
    /// RF-001/RF-002: persists the cost metric for the run. Telemetry must
    /// never break orchestration — failures are logged to the run stream.
    /// Returns the computed USD cost (0 on failure).
    /// </summary>
    private async Task<decimal> TryRecordUsageAsync(AgentExecutionRequest request, string runId, TokenUsage usage)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var finOps = scope.ServiceProvider.GetRequiredService<IFinOpsService>();
            var metric = await finOps.RecordUsageAsync(
                runId, request.AgentType, request.ResolvedModelName, usage,
                budgetCapUsd: request.MaxBudgetUsd,
                cancellationToken: CancellationToken.None);
            return metric.CostUsd;
        }
        catch (Exception ex)
        {
            AppendLog(request.IssueId, new AgentLogMessage(
                DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                $"FinOps usage recording failed: {ex.Message}"));
            return 0m;
        }
    }

    /// <summary>Recorded metrics + cost of the in-flight usage sample (RF-003).</summary>
    private async Task<decimal> TryComputeCumulativeCostAsync(AgentExecutionRequest request, string runId, TokenUsage pendingUsage)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var finOps = scope.ServiceProvider.GetRequiredService<IFinOpsService>();
            var recorded = await finOps.GetCumulativeCostAsync(runId, CancellationToken.None);
            return recorded + await finOps.ComputeCostAsync(request.ResolvedModelName, pendingUsage, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppendLog(request.IssueId, new AgentLogMessage(
                DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                $"FinOps cost computation failed: {ex.Message}"));
            return 0m;
        }
    }

    /// <summary>
    /// Opt-in post-run verification (RF-004/RF-005). The retry callback
    /// re-invokes the same agent with the structured failure prompt; exhausted
    /// retries surface as <c>EscalatedToHuman</c>. Returns null when the run
    /// did not request verification.
    /// </summary>
    private async Task<VerificationReportDto?> RunVerificationLoopAsync(
        AgentExecutionRequest request,
        WorktreeSessionDto? isolation,
        IProgress<AgentLogMessage> progress,
        string runId,
        CancellationToken cancellationToken)
    {
        if (request.VerifySolutionFile is not { } solutionFile)
        {
            return null;
        }

        var worktreePath = isolation?.Path ?? request.RepoPath;
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var loop = scope.ServiceProvider.GetRequiredService<IVerificationLoop>();

        var verifyRequest = new VerificationRunRequestDto(
            worktreePath,
            solutionFile,
            request.VerifyMinCoverage ?? 0.0,
            MaxAttempts: request.VerifyMaxAttempts ?? 3,
            RunId: runId,
            AgentType: request.AgentType,
            ModelName: request.ResolvedModelName);

        return await loop.RunAsync(verifyRequest, async (prompt, retryCt) =>
        {
            AppendLog(request.IssueId, new AgentLogMessage(
                DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                "Verification failed — re-invoking agent with structured feedback."));
            await _acpClient.ExecuteAsync(
                request with { Instructions = prompt }, progress, retryCt);
        }, cancellationToken);
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

            if (request.ExistingWorktreeRunId is { } existingId)
            {
                var existing = await isolation.GetAsync(existingId, cancellationToken);
                if (existing is not null)
                {
                    AppendLog(request.IssueId, new AgentLogMessage(
                        DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.System,
                        $"Reusing task worktree at {existing.Path} (branch {existing.Branch})."));
                }
                return existing;
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
    private async Task MarkWorktreeAsync(string worktreeRunId, bool completed)
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
        // Runs sem board (delegadas em worktree de task, scope-only) não têm
        // issue para mover — chamar o GitHub com repo vazio só logava erro.
        if (string.IsNullOrWhiteSpace(request.RepositoryFullName) || request.IssueNumber <= 0)
        {
            return;
        }

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

    private async Task UpdateRunAsync(Guid? runId, Func<IAgentRunRepository, Guid, Task> update)
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

        // SPEC-20260921-agent-execution-event-pipeline RF-003: every line also
        // enters the normalized persisted/sequenced stream.
        if (_eventSink is not null)
        {
            _ = _eventSink.EmitAsync(
                AgentEventNormalizer.FromLogMessage(message, AgentEventScope.Issue, issueId),
                CancellationToken.None);
        }
    }

    private sealed record QueuedJob(AgentExecutionRequest Request, Guid? RunId);

    private sealed class RunningJob
    {
        public RunningJob(AgentExecutionRequest request, CancellationTokenSource cancellationTokenSource)
        {
            Request = request;
            CancellationTokenSource = cancellationTokenSource;
            StartedAtUtc = DateTimeOffset.UtcNow;
        }

        public AgentExecutionRequest Request { get; }
        public CancellationTokenSource CancellationTokenSource { get; }
        /// <summary>SPEC-20261007 RF-005: when the job started running — busy-agent liveness.</summary>
        public DateTimeOffset StartedAtUtc { get; }
    }
}
