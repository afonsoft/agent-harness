using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Dtos;
using Taskboard.Harness;
using Taskboard.Harness.FinOps;
using Taskboard.Repositories;

namespace Taskboard.Application.Harness;

/// <summary>
/// Advances pipeline executions: promotes eligible stages, dispatches agent
/// work and verification steps over the shared run worktree, and chains the
/// next evaluation when a stage lands (SPEC-20260919-ade-multi-agent-orchestration
/// RF-002..RF-005).
/// </summary>
public sealed class PipelineEngine
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentAcpClient _acpClient;
    private readonly IVerificationEngine _verification;
    private readonly ILogger<PipelineEngine> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningStages = new();
    private readonly ConcurrentDictionary<Task, byte> _stageTasks = new();
    private readonly SemaphoreSlim _tick = new(1, 1);

    public PipelineEngine(
        IServiceScopeFactory scopeFactory,
        IAgentAcpClient acpClient,
        IVerificationEngine verification,
        ILogger<PipelineEngine> logger)
    {
        _scopeFactory = scopeFactory;
        _acpClient = acpClient;
        _verification = verification;
        _logger = logger;
    }

    /// <summary>Scans active executions and dispatches every eligible stage.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        await _tick.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await DispatchCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _tick.Release();
        }
    }

    /// <summary>Cancels all running stage tasks of an execution.</summary>
    public void CancelExecution(string pipelineExecutionId)
    {
        foreach (var (key, cts) in _runningStages)
        {
            if (key.StartsWith(pipelineExecutionId + "|", StringComparison.Ordinal))
            {
                cts.Cancel();
            }
        }
    }

    /// <summary>Awaits every spawned stage task, including chained ones (tests + graceful shutdown).</summary>
    internal async Task DrainAsync()
    {
        while (true)
        {
            var tasks = _stageTasks.Keys.ToArray();
            if (tasks.Length == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task<int> DispatchCoreAsync(CancellationToken cancellationToken)
    {
        var dispatched = 0;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PipelineExecution>>();
        var isolation = scope.ServiceProvider.GetRequiredService<IWorkspaceIsolationService>();
        var active = await repo.Query
            .Include(e => e.Stages)
            .Where(e => e.Status != PipelineStatus.Completed && e.Status != PipelineStatus.Cancelled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var finOpsDispatch = scope.ServiceProvider.GetService<IFinOpsService>();

        foreach (var exec in active)
        {
            // E14 RF-003: nunca despacha estágios novos quando o custo
            // acumulado da execução já passou do teto.
            if (exec.BudgetCapUsd is { } cap && finOpsDispatch is not null)
            {
                var cumulative = await finOpsDispatch.GetCumulativeCostAsync(exec.Id.Value, cancellationToken).ConfigureAwait(false);
                if (cumulative > cap)
                {
                    _logger.LogWarning(
                        "Pipeline {Id} cancelled — budget cap ${Cap} exceeded (${Cost} cumulative)",
                        exec.Id.Value, cap, cumulative);
                    exec.Cancel(DateTime.UtcNow);
                    await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            if (exec.WorktreePath is null && !await TryAttachWorktreeAsync(exec, repo, isolation, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var eligible = exec.EligibleStages();
            var toDispatch = new List<PipelineStageExecution>();
            foreach (var stage in eligible)
            {
                if (stage.Kind is PipelineStageKind.Approval)
                {
                    exec.MarkStageWaitingApproval(stage.StageKey);
                    continue;
                }

                var key = $"{exec.Id.Value}|{stage.StageKey}";
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (!_runningStages.TryAdd(key, cts))
                {
                    cts.Dispose();
                    continue;
                }

                exec.MarkStageRunning(stage.StageKey, DateTime.UtcNow);
                toDispatch.Add(stage);
            }

            // Persist transitions BEFORE spawning tasks — a task that reads the
            // execution before this save would see the stage still Pending.
            if (eligible.Count > 0)
            {
                await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var stage in toDispatch)
            {
                var key = $"{exec.Id.Value}|{stage.StageKey}";
                var cts = _runningStages[key];
                dispatched++;
                var task = RunStageAsync(exec.Id.Value, stage.StageKey, cts);
                _stageTasks.TryAdd(task, 0);
                _ = task.ContinueWith(
                    (done, self) => _stageTasks.TryRemove((Task)self!, out _),
                    task, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        return dispatched;
    }

    private async Task<bool> TryAttachWorktreeAsync(
        PipelineExecution exec,
        IRepository<PipelineExecution> repo,
        IWorkspaceIsolationService isolation,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await isolation.CreateWorktreeAsync(
                exec.Id.Value, exec.RepositoryPath, exec.BaseBranch,
                exec.TemplateId, retainOnFailure: true, cancellationToken).ConfigureAwait(false);
            exec.AttachWorktree(session.Path);
            await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pipeline {Id}: worktree isolation failed, skipping tick", exec.Id.Value);
            return false;
        }
    }

    private async Task RunStageAsync(string executionId, string stageKey, CancellationTokenSource cts)
    {
        var key = $"{executionId}|{stageKey}";
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<PipelineExecution>>();
            var exec = await LoadAsync(repo, executionId, cts.Token).ConfigureAwait(false);
            var stage = exec.Stages.Single(s => s.StageKey == stageKey);

            // SPEC-20260919-ade-observability-finops RF-004: stage span.
            using var stageActivity = HarnessTelemetrySource.StartStageSpan(
                executionId, stageKey, stage.Agent, modelName: null);

            TokenUsage? usage = null;
            if (stage.Kind is PipelineStageKind.Verification)
            {
                await RunVerificationStageAsync(exec, stage, cts.Token).ConfigureAwait(false);
            }
            else
            {
                usage = await RunAgentStageAsync(exec, stage, cts.Token).ConfigureAwait(false);
            }

            // RF-001/RF-002: per-stage cost metric; RF-003: over-cap cancels the
            // execution so dependent stages never start.
            var finOps = scope.ServiceProvider.GetService<IFinOpsService>();
            if (usage is not null && finOps is not null)
            {
                var metric = await finOps.RecordUsageAsync(
                    executionId, stage.Agent ?? AgentType.Codex, modelName: null, usage,
                    stageKey: stage.StageKey, budgetCapUsd: exec.BudgetCapUsd,
                    CancellationToken.None).ConfigureAwait(false);
                HarnessTelemetrySource.RecordUsage(stageActivity, usage, metric.CostUsd);

                if (exec.BudgetCapUsd is { } cap
                    && exec.Status is not (PipelineStatus.Completed or PipelineStatus.Cancelled))
                {
                    var cumulative = await finOps.GetCumulativeCostAsync(executionId, CancellationToken.None).ConfigureAwait(false);
                    if (cumulative > cap)
                    {
                        _logger.LogWarning(
                            "Pipeline {Id} cancelled — budget cap ${Cap} exceeded (${Cost} cumulative)",
                            executionId, cap, cumulative);
                        exec.Cancel(DateTime.UtcNow);
                    }
                }
            }

            await repo.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pipeline {Id} stage {Stage} crashed", executionId, stageKey);
            await TryFailStageAsync(executionId, stageKey, ex.Message).ConfigureAwait(false);
        }
        finally
        {
            cts.Dispose();
            _runningStages.TryRemove(key, out _);
            await DispatchPendingAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<TokenUsage?> RunAgentStageAsync(
        PipelineExecution exec, PipelineStageExecution stage, CancellationToken cancellationToken)
    {
        var chunks = new List<string>();
        var progress = new SyncProgress(chunks);
        var request = new AgentExecutionRequest(
            IssueId: exec.IssueId ?? exec.Id.Value,
            IssueNumber: 0,
            RepositoryFullName: exec.RepositoryFullName,
            RepoPath: exec.WorktreePath ?? exec.RepositoryPath,
            Branch: null,
            Scope: $"pipeline:{exec.TemplateId}/{stage.StageKey}",
            Instructions: PipelineContextSynthesizer.BuildStagePrompt(exec, stage),
            AgentType: stage.Agent ?? AgentType.Codex,
            ModelTier: stage.ModelTier);

        var result = await _acpClient.ExecuteAsync(request, progress, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        if (result.IsSuccess)
        {
            exec.CompleteStage(stage.StageKey, PipelineContextSynthesizer.SummarizeOutput(chunks), now);
        }
        else
        {
            exec.FailStage(stage.StageKey, $"Agent exited with code {result.ExitCode}", now);
        }

        // RF-001: usage reportado no resultado ou varrido das linhas de stdout.
        TokenUsage? usage = result.Usage;
        if (usage is null)
        {
            lock (chunks)
            {
                foreach (var line in chunks)
                {
                    if (TokenUsageParser.TryExtract(line) is { } parsed)
                    {
                        usage = parsed; // última linha com usage vence (contadores cumulativos)
                    }
                }
            }
        }

        return usage;
    }

    private async Task RunVerificationStageAsync(
        PipelineExecution exec, PipelineStageExecution stage, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var worktree = exec.WorktreePath;
        var solution = worktree is null
            ? null
            : Directory.EnumerateFiles(worktree, "*.sln")
                .Concat(Directory.EnumerateFiles(worktree, "*.slnx"))
                .FirstOrDefault();
        if (solution is null)
        {
            exec.FailStage(stage.StageKey, "No .sln/.slnx found in the run worktree.", now);
            return;
        }

        var report = await _verification.RunAsync(
            new VerificationRunRequestDto(worktree!, solution, MinCoverageThreshold: 0),
            cancellationToken).ConfigureAwait(false);
        if (report.IsSuccess)
        {
            exec.CompleteStage(stage.StageKey, $"Verification passed — coverage {report.CoveragePercent}%", now);
        }
        else
        {
            exec.FailStage(stage.StageKey, report.FeedbackPrompt ?? $"Verification failed: {report.Status}", now);
        }
    }

    private async Task TryFailStageAsync(string executionId, string stageKey, string error)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<PipelineExecution>>();
            var exec = await LoadAsync(repo, executionId, CancellationToken.None).ConfigureAwait(false);
            exec.FailStage(stageKey, error, DateTime.UtcNow);
            await repo.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pipeline {Id}: could not persist stage failure", executionId);
        }
    }

    private static Task<PipelineExecution> LoadAsync(
        IRepository<PipelineExecution> repo, string executionId, CancellationToken cancellationToken) =>
        repo.Query
            .Include(e => e.Stages)
            .SingleAsync(e => e.Id == PipelineExecutionId.From(executionId), cancellationToken);

    /// <summary>Synchronous IProgress — deterministic handoff capture (Progress&lt;T&gt; defers callbacks).</summary>
    private sealed class SyncProgress(List<string> chunks) : IProgress<AgentLogMessage>
    {
        public void Report(AgentLogMessage value)
        {
            lock (chunks)
            {
                chunks.Add(value.Content);
            }
        }
    }
}
