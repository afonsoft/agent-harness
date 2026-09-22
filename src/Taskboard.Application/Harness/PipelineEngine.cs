using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
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
    private readonly ICockpitEventStream? _cockpit;
    private readonly ISteerQueue? _steer;
    private readonly IAgentExecutionEventSink? _eventSink;
    private readonly ILogger<PipelineEngine> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningStages = new();
    private readonly ConcurrentDictionary<Task, byte> _stageTasks = new();
    private readonly SemaphoreSlim _tick = new(1, 1);

    public PipelineEngine(
        IServiceScopeFactory scopeFactory,
        IAgentAcpClient acpClient,
        IVerificationEngine verification,
        ILogger<PipelineEngine> logger,
        ICockpitEventStream? cockpit = null,
        ISteerQueue? steer = null,
        IAgentExecutionEventSink? eventSink = null)
    {
        _scopeFactory = scopeFactory;
        _acpClient = acpClient;
        _verification = verification;
        _logger = logger;
        _cockpit = cockpit;
        _steer = steer;
        _eventSink = eventSink;
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
        // SPEC-20260920-cockpit-pause-resume: Paused executions are skipped
        // entirely — no dispatch, no worktree attach, no budget-cap cancel —
        // until Resume returns them to a live status.
        var active = await repo.Query
            .Include(e => e.Stages)
            .Where(e => e.Status != PipelineStatus.Completed
                && e.Status != PipelineStatus.Cancelled
                && e.Status != PipelineStatus.Paused)
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
            var approvals = new List<PipelineStageExecution>();
            foreach (var stage in eligible)
            {
                if (stage.Kind is PipelineStageKind.Approval)
                {
                    exec.MarkStageWaitingApproval(stage.StageKey);
                    approvals.Add(stage);
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

            // SPEC-20260919-ade-cockpit-hitl RF-001/RF-004: stage transitions
            // and approval gates stream to the run's cockpit group.
            foreach (var stage in approvals)
            {
                await PublishApprovalAsync(exec, stage).ConfigureAwait(false);
            }

            foreach (var stage in toDispatch)
            {
                await PublishAsync(exec, "stage", $"Stage '{stage.Name}' started", stage.StageKey)
                    .ConfigureAwait(false);
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

            if (stage.Kind is PipelineStageKind.Verification)
            {
                await RunVerificationStageAsync(exec, stage, cts.Token).ConfigureAwait(false);
            }
            else
            {
                await RunAgentStageAsync(
                    exec, stage, scope.ServiceProvider, repo, stageActivity, cts.Token)
                    .ConfigureAwait(false);
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

    /// <summary>
    /// Runs an AgentWork stage through the CLI fallback chain
    /// (SPEC-20260922-cockpit-agent-selection-fallback RF-003/RF-004): the
    /// stage's bound CLI first, then every other eligible CLI not yet tried —
    /// same synthesized prompt/context on each attempt, usage billed to the
    /// CLI that actually ran. Exhaustion keeps the existing FailStage path.
    /// </summary>
    private async Task RunAgentStageAsync(
        PipelineExecution exec,
        PipelineStageExecution stage,
        IServiceProvider services,
        IRepository<PipelineExecution> repo,
        Activity? stageActivity,
        CancellationToken cancellationToken)
    {
        var chunks = new List<string>();
        var runId = exec.Id.Value;
        var stageKey = stage.StageKey;
        var progress = new SyncProgress(chunks, message =>
        {
            // SPEC-20260919-ade-cockpit-hitl RF-001: every agent output chunk is
            // a cockpit event (fire-and-forget — a dead group never stalls a stage).
            if (_cockpit is not null)
            {
                _ = _cockpit.PublishAsync(new CockpitEventDto(
                    runId,
                    message.Timestamp,
                    "agent_output",
                    stageKey,
                    JsonSerializer.Serialize(
                        new { stream = message.Stream.ToString(), content = message.Content },
                        JsonOptions)));
            }

            // SPEC-20260921-agent-execution-event-pipeline RF-003: durable
            // normalized stream (tool_call/plan/output) alongside the cockpit.
            EmitNormalized(exec, AgentEventNormalizer.FromLogMessage(
                message, AgentEventScope.Run, runId, stageId: stageKey));
        });

        var instructions = PipelineContextSynthesizer.BuildStagePrompt(exec, stage);

        // RF-003: queued human-steer instructions are folded into the next
        // dispatched stage prompt (one-shot agents have no live injection).
        if (_steer is not null)
        {
            var steers = _steer.Drain(runId);
            if (steers.Count > 0)
            {
                instructions += "\n\n# Human steer (mid-run corrections)\n"
                    + string.Join('\n', steers.Select(s => $"- {s}"));
            }
        }

        var eligibility = services.GetService<IAgentEligibilityService>();
        var modelConfig = services.GetService<IAgentModelConfigService>();
        var catalog = services.GetService<IAgentModelCatalogService>();
        var finOps = services.GetService<IFinOpsService>();

        var eligible = eligibility is null
            ? null
            : await eligibility.GetEligibleTypesAsync(cancellationToken).ConfigureAwait(false);

        string? lastError = null;
        foreach (var candidate in FallbackCandidates(stage, eligible))
        {
            if (stage.Agent != candidate)
            {
                var previous = stage.TriedAgents.LastOrDefault()
                    ?? stage.Agent?.ToString()
                    ?? "?";
                exec.BeginStageFallback(stageKey, candidate);
                await PublishAsync(exec, "stage",
                    $"Stage '{stage.Name}' — {previous} failed ({lastError}); retrying with {candidate}",
                    stageKey).ConfigureAwait(false);
            }

            // Eligibility is dynamic — a CLI disabled mid-run is skipped, never dispatched.
            if (eligible is not null && !eligible.Contains(candidate))
            {
                lastError = $"Agent {candidate} is not eligible (disabled or CLI not authenticated).";
                exec.RecordStageAttemptFailure(stageKey, lastError);
                await repo.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            var (model, omitFlag) = await ResolveStageModelAsync(
                    candidate, stage.ModelTier, modelConfig, catalog,
                    exec, stage.Name, stageKey, cancellationToken)
                .ConfigureAwait(false);

            var retriedWithoutFlag = false;
            while (true)
            {
                var chunkOffset = chunks.Count;
                var request = new AgentExecutionRequest(
                    IssueId: exec.IssueId ?? exec.Id.Value,
                    IssueNumber: 0,
                    RepositoryFullName: exec.RepositoryFullName,
                    RepoPath: exec.WorktreePath ?? exec.RepositoryPath,
                    Branch: null,
                    Scope: $"pipeline:{exec.TemplateId}/{stage.StageKey}",
                    Instructions: instructions,
                    AgentType: candidate,
                    ModelTier: stage.ModelTier,
                    ResolvedModelName: model,
                    OmitModelFlag: omitFlag);

                AgentExecutionResult? result = null;
                try
                {
                    result = await _acpClient.ExecuteAsync(request, progress, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }

                // SPEC-20260922 §8: every attempt bills under the CLI that ran
                // it — usage from the result or scanned from the output tail.
                var attemptUsage = result?.Usage ?? ScanUsage(chunks, chunkOffset);
                if (attemptUsage is not null && finOps is not null)
                {
                    var metric = await finOps.RecordUsageAsync(
                        runId, candidate, result?.ModelUsed, attemptUsage,
                        stageKey: stageKey, budgetCapUsd: exec.BudgetCapUsd,
                        CancellationToken.None).ConfigureAwait(false);
                    HarnessTelemetrySource.RecordUsage(stageActivity, attemptUsage, metric.CostUsd);

                    // E14 RF-003: over-cap cancels the execution — no further
                    // attempt or dependent stage is dispatched.
                    if (exec.BudgetCapUsd is { } cap
                        && exec.Status is not (PipelineStatus.Completed or PipelineStatus.Cancelled))
                    {
                        var cumulative = await finOps.GetCumulativeCostAsync(runId, CancellationToken.None)
                            .ConfigureAwait(false);
                        if (cumulative > cap)
                        {
                            _logger.LogWarning(
                                "Pipeline {Id} cancelled — budget cap ${Cap} exceeded (${Cost} cumulative)",
                                runId, cap, cumulative);
                            exec.Cancel(DateTime.UtcNow);
                            return;
                        }
                    }
                }

                if (result is null)
                {
                    break;
                }

                if (result.IsSuccess)
                {
                    exec.CompleteStage(
                        stageKey, PipelineContextSynthesizer.SummarizeOutput(chunks), DateTime.UtcNow);
                    await PublishAsync(exec, "stage", $"Stage '{stage.Name}' completed", stageKey)
                        .ConfigureAwait(false);
                    return;
                }

                lastError = $"Agent exited with code {result.ExitCode}";

                // RF-004: a model-catalog rejection retries once on the same
                // CLI without a model flag before consuming a fallback CLI.
                if (!retriedWithoutFlag && !omitFlag && OutputSuggestsInvalidModel(chunks, chunkOffset))
                {
                    retriedWithoutFlag = true;
                    omitFlag = true;
                    model = null;
                    await PublishAsync(exec, "stage",
                        $"Stage '{stage.Name}' — {candidate} rejected the model; retrying with the CLI default",
                        stageKey).ConfigureAwait(false);
                    continue;
                }

                break;
            }

            exec.RecordStageAttemptFailure(stageKey, lastError ?? "agent failed");
            await repo.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        exec.FailStage(
            stageKey,
            lastError ?? "No eligible agent CLI could run the stage.",
            DateTime.UtcNow);
        await PublishAsync(exec, "stage", $"Stage '{stage.Name}' failed ({lastError})", stageKey)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// CLI chain for a stage: the bound agent first (when untried), then the
    /// remaining eligible CLIs in enum order — each tried at most once
    /// (SPEC-20260922 RF-003). Without an eligibility service the chain is the
    /// bound CLI only.
    /// </summary>
    private static List<AgentType> FallbackCandidates(
        PipelineStageExecution stage, IReadOnlySet<AgentType>? eligible)
    {
        var tried = new HashSet<string>(stage.TriedAgents, StringComparer.Ordinal);
        var current = stage.Agent ?? AgentType.Codex;
        var candidates = new List<AgentType>();
        if (!tried.Contains(current.ToString()))
        {
            candidates.Add(current);
        }

        if (eligible is not null)
        {
            foreach (var type in Enum.GetValues<AgentType>())
            {
                if (eligible.Contains(type) && type != current && !tried.Contains(type.ToString()))
                {
                    candidates.Add(type);
                }
            }
        }

        return candidates;
    }

    /// <summary>
    /// Resolves the model passed to the CLI: per-tier override ?? curated
    /// table, validated against the CLI's probed catalog when it has one —
    /// a name the installed CLI does not report is dropped so the run uses
    /// the CLI default instead of dying on `unrecognized_model`
    /// (SPEC-20260922 RF-004).
    /// </summary>
    private async Task<(string? Model, bool OmitFlag)> ResolveStageModelAsync(
        AgentType candidate,
        AgentModelTier tier,
        IAgentModelConfigService? modelConfig,
        IAgentModelCatalogService? catalog,
        PipelineExecution exec,
        string stageName,
        string stageKey,
        CancellationToken cancellationToken)
    {
        var resolved = modelConfig is null
            ? AgentCliModels.ModelFor(candidate, tier)
            : await modelConfig.ResolveModelAsync(candidate, tier, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return (null, true);
        }

        if (catalog is null || AgentCliModels.ModelListProbe(candidate) is null)
        {
            return (resolved, false);
        }

        var available = await catalog.ListAvailableAsync(candidate, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (available.Count > 0 && !available.Contains(resolved, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Pipeline {Id} stage {Stage}: resolved model '{Model}' is not in {Agent}'s probed catalog — dispatching with the CLI default.",
                exec.Id.Value, stageKey, resolved, candidate);
            await PublishAsync(exec, "stage",
                $"Stage '{stageName}' — model '{resolved}' is not in {candidate}'s catalog; using the CLI default",
                stageKey).ConfigureAwait(false);
            return (null, true);
        }

        return (resolved, false);
    }

    /// <summary>RF-004: detects the `unrecognized_model` / stale-catalog signature in the attempt's output tail.</summary>
    private static bool OutputSuggestsInvalidModel(List<string> chunks, int offset)
    {
        lock (chunks)
        {
            for (var i = offset; i < chunks.Count; i++)
            {
                if (chunks[i].Contains("unrecognized_model", StringComparison.OrdinalIgnoreCase)
                    || chunks[i].Contains("model catalog", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>RF-001: usage varrido das linhas de stdout da tentativa (última linha com usage vence).</summary>
    private static TokenUsage? ScanUsage(List<string> chunks, int offset)
    {
        TokenUsage? usage = null;
        lock (chunks)
        {
            for (var i = offset; i < chunks.Count; i++)
            {
                if (TokenUsageParser.TryExtract(chunks[i]) is { } parsed)
                {
                    usage = parsed;
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

        // SPEC-20260919-ade-cockpit-hitl RF-001: verification outcomes land on
        // the cockpit timeline as `verification` cards.
        await PublishAsync(
            exec,
            "verification",
            report.IsSuccess
                ? $"Verification passed — coverage {report.CoveragePercent}%"
                : $"Verification failed: {report.Status}",
            stage.StageKey).ConfigureAwait(false);
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
    private sealed class SyncProgress(List<string> chunks, Action<AgentLogMessage>? onMessage = null) : IProgress<AgentLogMessage>
    {
        public void Report(AgentLogMessage value)
        {
            lock (chunks)
            {
                chunks.Add(value.Content);
            }

            onMessage?.Invoke(value);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Emits the normalized event under <c>run:{exec.Id}</c> and, when the
    /// execution is bound to a board issue, mirrors it under <c>issue:{IssueId}</c> —
    /// the Board task log reads the issue scope
    /// (SPEC-20260921-board-cockpit-agent-observability RF-002).
    /// </summary>
    private void EmitNormalized(PipelineExecution exec, AgentExecutionEvent evt)
    {
        if (_eventSink is null)
        {
            return;
        }

        _ = _eventSink.EmitAsync(evt, CancellationToken.None);
        if (!string.IsNullOrEmpty(exec.IssueId))
        {
            _ = _eventSink.EmitAsync(
                evt with { ScopeKind = AgentEventScope.Issue, ScopeId = exec.IssueId },
                CancellationToken.None);
        }
    }

    private async Task PublishAsync(PipelineExecution exec, string kind, string title, string? stageKey)
    {
        var runId = exec.Id.Value;

        // SPEC-20260921-agent-execution-event-pipeline: cockpit kinds map to
        // the normalized taxonomy — the durable event goes out even without a cockpit.
        var normalizedKind = kind switch
        {
            "stage" or "status" => AgentEventKinds.Lifecycle,
            "verification" => AgentEventKinds.Verification,
            "steer" => AgentEventKinds.Steer,
            "diff" => AgentEventKinds.Diff,
            "approval" => AgentEventKinds.Approval,
            _ => AgentEventKinds.Activity
        };
        EmitNormalized(exec, new AgentExecutionEvent(
            string.Empty, AgentEventScope.Run, runId, 0, DateTimeOffset.UtcNow,
            normalizedKind, stageKey, Title: title));

        if (_cockpit is null)
        {
            return;
        }

        try
        {
            await _cockpit.PublishAsync(new CockpitEventDto(runId, DateTimeOffset.UtcNow, kind, title, stageKey))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cockpit publish failed for run {RunId} ({Kind}).", runId, kind);
        }
    }

    private async Task PublishApprovalAsync(PipelineExecution exec, PipelineStageExecution stage)
    {
        var runId = exec.Id.Value;

        // The `stage:` requestId prefix is how the approvals endpoint resolves
        // the reply back to the stage gate (SPEC-20260919-ade-cockpit-hitl §5).
        EmitNormalized(exec, new AgentExecutionEvent(
            string.Empty, AgentEventScope.Run, runId, 0, DateTimeOffset.UtcNow,
            AgentEventKinds.Approval, stage.StageKey,
            Title: $"Stage '{stage.Name}' awaits approval",
            PayloadJson: JsonSerializer.Serialize(new { requestId = $"stage:{stage.StageKey}", options = new[] { "Allow", "Deny" } }, JsonOptions)));

        if (_cockpit is null)
        {
            return;
        }

        try
        {
            await _cockpit.PublishApprovalAsync(new ApprovalRequestDto(
                runId,
                $"stage:{stage.StageKey}",
                $"Stage '{stage.Name}' awaits approval",
                $"Pipeline stage '{stage.Name}' ({stage.StageKey}) requires human approval to proceed.",
                ["Allow", "Deny"])).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cockpit approval publish failed for run {RunId} stage {Stage}.", runId, stage.StageKey);
        }
    }
}
