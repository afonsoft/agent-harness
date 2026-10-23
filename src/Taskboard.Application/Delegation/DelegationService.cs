using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;

namespace Taskboard.Application.Delegation;

/// <summary>
/// SPEC-20261005: business rules over the delegation DAG + mailbox.
/// The repository owns entity transitions; this service owns the DAG verdict
/// (pending → ready | cancelled), create-time validation, heartbeat sweep and
/// the finalize+mailbox coupling.
/// </summary>
public sealed class DelegationService : IDelegationService
{
    private const int DetailMaxLength = 4000;
    private static readonly TimeSpan DefaultCheckWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckWaitPoll = TimeSpan.FromSeconds(1);

    private readonly IDelegationTaskRepository _tasks;
    private readonly IAgentMailboxRepository _mailbox;
    private readonly IDelegationRunRepository _runs;
    private readonly IDispatchRepository _dispatches;
    private readonly IDeliveryRepository _deliveries;
    private readonly IPendingQuestionRepository _questions;
    private readonly IDelegationGateRepository _gates;
    private readonly IAgentAttentionService? _attention;
    private readonly ILogger<DelegationService>? _logger;
    private readonly TimeProvider _time;
    private readonly IConfiguration _configuration;
    private readonly ITaskPromptRefiner _refiner;

    public DelegationService(
        IDelegationTaskRepository tasks,
        IAgentMailboxRepository mailbox,
        IDelegationRunRepository runs,
        IDispatchRepository dispatches,
        IDeliveryRepository deliveries,
        IPendingQuestionRepository questions,
        IDelegationGateRepository gates,
        IConfiguration configuration,
        ITaskPromptRefiner refiner,
        TimeProvider? timeProvider = null,
        IAgentAttentionService? attention = null,
        ILogger<DelegationService>? logger = null)
    {
        _tasks = tasks;
        _mailbox = mailbox;
        _runs = runs;
        _dispatches = dispatches;
        _deliveries = deliveries;
        _questions = questions;
        _gates = gates;
        _attention = attention;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _configuration = configuration;
        _refiner = refiner;
    }

    /// <summary>SPEC-20261020-agent-attention-model: lifecycle/mailbox signal emit — fire-and-log.</summary>
    private async Task EmitAttentionAsync(
        string sessionKey, string cli, AgentAttentionState state,
        AttentionSignalKind kind, string? reason, string? evidence,
        CancellationToken ct)
    {
        if (_attention is null)
        {
            return;
        }

        try
        {
            await _attention.PublishAsync(
                new AttentionSignal(sessionKey, cli, kind, state, reason, evidence), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Attention is advisory — never break the delegation path on a signal failure.
            _logger?.LogWarning(ex, "attention signal failed for {SessionKey}", sessionKey);
        }
    }

    public async Task<DelegationTaskDto> CreateTaskAsync(
        CreateDelegationTaskRequest request, CancellationToken ct = default)
    {
        var scope = string.IsNullOrWhiteSpace(request.Scope) ? "harness" : request.Scope.Trim();
        var dependsOn = request.DependsOn ?? [];

        if (dependsOn.Count > 0)
        {
            var deps = await _tasks.ListByIdsAsync(dependsOn, scope, ct).ConfigureAwait(false);
            var known = deps.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            var missing = dependsOn.FirstOrDefault(d => !known.Contains(d));
            if (missing is not null)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"unknown dependency: {missing}");
            }
        }

        if (request.RetryOf is { } retryOf)
        {
            // SPEC-20261020-v2 RF-007: retry reactivates the same task — a new
            // dispatch attempt replaces the legacy "clone task" behavior.
            return (await RetryTaskAsync(scope, retryOf, request.Refine, ct)
                .ConfigureAwait(false)).Task;
        }

        var run = await EnsureRunAsync(scope, request.Prompt, null, ct).ConfigureAwait(false);
        var (refinedRequest, refineResult) = await RefineAsync(
            request with { Scope = scope, RunId = run.Id }, ct).ConfigureAwait(false);
        var task = await _tasks.AddAsync(refinedRequest, ct).ConfigureAwait(false);
        await EmitRefinedEventAsync(scope, task.Id, request.Prompt, refineResult, ct)
            .ConfigureAwait(false);
        return task;
    }

    /// <summary>SPEC-20261022-task-prompt-refiner RF-001/004: intercepts
    /// before persist — prompt stays the submitted text, PromptRefined is
    /// what the worker receives. Refinement never blocks creation.</summary>
    private async Task<(CreateDelegationTaskRequest Request, TaskPromptRefineResult? Result)>
        RefineAsync(CreateDelegationTaskRequest request, CancellationToken ct)
    {
        if (request.PromptRefined is not null)
        {
            return (request, null); // already refined by the caller (e.g. automation fire)
        }

        var refineMode = TaskPromptRefinementModes.IsValidOverride(request.Refine)
            ? request.Refine!
            : _configuration[TaskPromptRefinementModes.ConfigurationKey]
                ?? TaskPromptRefinementModes.Default;
        if (refineMode == TaskPromptRefinementModes.Off)
        {
            return (request, null);
        }

        try
        {
            var result = await _refiner.RefineAsync(
                new TaskPromptRefineRequest(
                    request.Prompt,
                    request.CliName,
                    request.RepositoryPath,
                    request.UseWorktree ? request.WorkspacePath : null,
                    request.IssueRef,
                    PriorFailureDetail: null,
                    Attempt: 1,
                    Mode: request.Refine),
                ct).ConfigureAwait(false);
            return (request with
            {
                PromptRefined = result.Refined,
                PromptRefinement = result.Mode,
            }, result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Refinement is best-effort — creation must never fail on it.
            _logger?.LogWarning(ex, "prompt refinement skipped for {Scope}", request.Scope);
            return (request, null);
        }
    }

    /// <summary>SPEC-20261022-task-prompt-refiner RF-008: refined_prompt
    /// diagnostics event on the scope mailbox — surfaces in the merged
    /// delegation events feed. Advisory; never breaks the write path.</summary>
    private async Task EmitRefinedEventAsync(
        string scope, string taskId, string rawPrompt,
        TaskPromptRefineResult? result, CancellationToken ct)
    {
        if (result is null || result.Mode == TaskPromptRefinementModes.Off)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                taskId,
                mode = result.Mode,
                anchors = result.SpecAnchors.Count,
                byteDelta = result.Refined.Length - rawPrompt.Length,
            });
            await _mailbox.AddAsync(
                new PostMailboxMessageRequest(
                    scope, "harness", "coordinator", payload,
                    AgentMailboxKinds.RefinedPrompt, taskId),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "refined_prompt event failed for {TaskId}", taskId);
        }
    }

    public async Task<DelegationRunDto> EnsureRunAsync(
        string scope, string? objective = null, string? coordinatorKey = null,
        CancellationToken ct = default)
    {
        var existing = await _runs.GetOpenByScopeAsync(scope, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        return await _runs.AddAsync(
            scope,
            Truncate(objective, 200),
            coordinatorKey ?? "assistant",
            ct).ConfigureAwait(false);
    }

    public async Task<DelegationRunShowDto?> GetRunShowAsync(
        string? runId, string scope, CancellationToken ct = default)
    {
        var run = runId is not null
            ? await _runs.GetAsync(runId, ct).ConfigureAwait(false)
            : await _runs.GetOpenByScopeAsync(scope, ct).ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        var tasks = (await _tasks.ListByScopeAsync(run.Scope, take: 200, ct).ConfigureAwait(false))
            .Where(t => string.Equals(t.RunId, run.Id, StringComparison.Ordinal))
            .ToList();
        var openDispatches = await _dispatches.ListOpenByRunAsync(run.Id, ct).ConfigureAwait(false);
        var unacked = (await _deliveries.ListByRunAsync(run.Id, 100, ct).ConfigureAwait(false))
            .Where(d => d.AckedAtUtc is null)
            .ToList();
        return new DelegationRunShowDto(run, tasks, openDispatches, unacked);
    }

    public async Task<RetryTaskResult> RetryTaskAsync(
        string scope, string retryOf, string? refineOverride = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(retryOf))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "retry_of is required");
        }

        string taskId;
        if (retryOf.StartsWith("disp-", StringComparison.Ordinal))
        {
            var origin = await _dispatches.GetAsync(retryOf, ct).ConfigureAwait(false);
            if (origin is null)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"unknown retry_of: {retryOf}");
            }

            taskId = origin.TaskId;
        }
        else
        {
            taskId = retryOf;
        }

        var targets = await _tasks.ListByIdsAsync([taskId], scope, ct).ConfigureAwait(false);
        var task = targets.FirstOrDefault();
        if (task is null)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"unknown retry_of: {retryOf}");
        }

        if (task.Status is not (DelegationTaskStatus.Failed
            or DelegationTaskStatus.Stale or DelegationTaskStatus.Cancelled))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"retry_of target is not retryable (status {task.Status})");
        }

        var attempt = await _dispatches.LatestAttemptAsync(taskId, ct).ConfigureAwait(false) + 1;
        var reactivated = await _tasks.ReactivateForRetryAsync(taskId, ct).ConfigureAwait(false)
            ?? throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"task {taskId} is not retryable");

        // SPEC-20261022-task-prompt-refiner RF-005: the retry brief carries the
        // previous terminal detail as fix-forward context (AC-02).
        reactivated = await RefineForRetryAsync(
            reactivated, task, attempt, refineOverride, ct).ConfigureAwait(false);

        var runId = reactivated.RunId
            ?? (await EnsureRunAsync(scope, null, null, ct).ConfigureAwait(false)).Id;
        var dispatchId = $"disp-{Guid.NewGuid():N}";
        var dispatch = await _dispatches.AddAsync(
            new CreateDispatchRequest(
                taskId, runId, reactivated.CliName, attempt,
                BuildPreamble(reactivated, dispatchId, runId, attempt),
                dispatchId),
            ct).ConfigureAwait(false);
        return new RetryTaskResult(reactivated, dispatch);
    }

    /// <summary>SPEC-20261022-task-prompt-refiner RF-005: re-refines the raw
    /// prompt with <c>## Attempt context</c> — the prior failure's terminal
    /// detail (≤ 1 KB). Mode <c>off</c> or a refiner failure keeps the stored
    /// refined prompt untouched.</summary>
    private async Task<DelegationTaskDto> RefineForRetryAsync(
        DelegationTaskDto reactivated, DelegationTaskDto failed, int attempt,
        string? refineOverride, CancellationToken ct)
    {
        var mode = TaskPromptRefinementModes.IsValidOverride(refineOverride)
            ? refineOverride!
            : _configuration[TaskPromptRefinementModes.ConfigurationKey]
                ?? TaskPromptRefinementModes.Default;
        if (mode == TaskPromptRefinementModes.Off)
        {
            return reactivated;
        }

        var priorDetail = string.Join(
            '\n',
            new[] { failed.Error, failed.ResultSummary }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        try
        {
            // IssueRef isn't persisted — recover the #<n> anchor from the raw
            // prompt of issue-kind tasks so retry keeps spec anchoring.
            var issueRef = failed.Kind == "issue"
                ? System.Text.RegularExpressions.Regex.Match(
                    failed.Prompt, @"#\d+",
                    System.Text.RegularExpressions.RegexOptions.None,
                    TimeSpan.FromMilliseconds(100)).Value
                : null;

            var result = await _refiner.RefineAsync(
                new TaskPromptRefineRequest(
                    failed.Prompt,
                    failed.CliName,
                    failed.RepositoryPath,
                    failed.UseWorktree ? failed.WorkspacePath : null,
                    string.IsNullOrEmpty(issueRef) ? null : issueRef,
                    string.IsNullOrWhiteSpace(priorDetail) ? null : priorDetail,
                    attempt,
                    Mode: refineOverride),
                ct).ConfigureAwait(false);
            if (result.Mode == TaskPromptRefinementModes.Off)
            {
                return reactivated;
            }

            var updated = await _tasks.ApplyRefinementAsync(
                reactivated.Id, result.Refined, result.Mode, ct).ConfigureAwait(false);
            await EmitRefinedEventAsync(
                reactivated.Scope, reactivated.Id, failed.Prompt, result, ct)
                .ConfigureAwait(false);
            return updated ?? reactivated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "retry refinement skipped for {TaskId}", failed.Id);
            return reactivated;
        }
    }

    public Task<DelegationTaskDto?> AttachWorktreeAsync(
        string taskId, string worktreeRunId,
        IReadOnlyList<string>? sharedPathsApplied = null,
        string? setupState = null,
        string? setupLogTail = null,
        CancellationToken ct = default) =>
        _tasks.AttachWorktreeAsync(taskId, worktreeRunId, sharedPathsApplied, setupState, setupLogTail, ct);

    public Task<IReadOnlyList<DelegationTaskDto>> ListTasksAsync(
        string scope, int take = 50, CancellationToken ct = default) =>
        _tasks.ListByScopeAsync(scope, take, ct);

    public Task<IReadOnlyList<DelegationTaskDto>> ListOpenAsync(CancellationToken ct = default) =>
        _tasks.ListOpenAsync(ct);

    public async Task<int> ResolveDependenciesAsync(CancellationToken ct = default)
    {
        var open = await _tasks.ListOpenAsync(ct).ConfigureAwait(false);
        var changed = 0;

        foreach (var group in open
            .Where(t => t.Status == DelegationTaskStatus.Pending)
            .GroupBy(t => t.Scope))
        {
            var depIds = group.SelectMany(t => t.DependsOn).Distinct(StringComparer.Ordinal).ToList();
            var deps = depIds.Count == 0
                ? new Dictionary<string, DelegationTaskDto>(StringComparer.Ordinal)
                : (await _tasks.ListByIdsAsync(depIds, group.Key, ct).ConfigureAwait(false))
                    .ToDictionary(d => d.Id, StringComparer.Ordinal);

            foreach (var task in group)
            {
                var verdict = EvaluateDeps(task, deps);
                if (verdict == Verdict.StillPending)
                {
                    continue;
                }

                if (verdict == Verdict.Ready)
                {
                    await _tasks.MarkReadyAsync(task.Id, ct).ConfigureAwait(false);
                }
                else
                {
                    await _tasks.FinalizeAsync(
                        task.Id, DelegationTaskStatus.Cancelled,
                        CancelDetail(task, deps),
                        _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
                }

                changed++;
            }
        }

        return changed;
    }

    public async Task<int> SweepStaleHeartbeatsAsync(
        TimeSpan timeout, DateTime now, CancellationToken ct = default)
    {
        var open = await _tasks.ListOpenAsync(ct).ConfigureAwait(false);
        var swept = 0;
        foreach (var task in open.Where(t =>
            t.Status == DelegationTaskStatus.Running
            && (t.LastHeartbeatAt is null || now - t.LastHeartbeatAt.Value > timeout)))
        {
            await FinishRunAsync(task.Id, succeeded: false, "dispatcher lost (heartbeat timeout)", ct)
                .ConfigureAwait(false);
            swept++;
        }

        return swept;
    }

    public async Task<DelegationTaskDto?> BeginRunAsync(string id, DateTime now, CancellationToken ct = default)
    {
        var task = await _tasks.MarkRunningAsync(id, now, ct).ConfigureAwait(false);
        if (task is null)
        {
            return null;
        }

        // SPEC-20261020-v2 RF-002: a running task is always bound to a dispatch
        // attempt — a pending one created by retry, or a fresh attempt here.
        var existing = await _dispatches.ListByTaskAsync(id, ct).ConfigureAwait(false);
        var pending = existing.LastOrDefault(d => d.State == DispatchState.Pending);
        DispatchDto? dispatch;
        if (pending is not null)
        {
            dispatch = await _dispatches.TransitionAsync(
                pending.Id, DispatchState.Dispatched, null, now, ct).ConfigureAwait(false) ?? pending;
        }
        else
        {
            var runId = task.RunId
                ?? (await EnsureRunAsync(task.Scope, null, null, ct).ConfigureAwait(false)).Id;
            var attempt = existing.Count == 0 ? 1 : existing.Max(d => d.Attempt) + 1;
            var dispatchId = $"disp-{Guid.NewGuid():N}";
            var created = await _dispatches.AddAsync(
                new CreateDispatchRequest(
                    id, runId, task.CliName, attempt,
                    BuildPreamble(task, dispatchId, runId, attempt),
                    dispatchId),
                ct).ConfigureAwait(false);
            dispatch = await _dispatches.TransitionAsync(
                created.Id, DispatchState.Dispatched, null, now, ct).ConfigureAwait(false) ?? created;
        }

        task = await _tasks.AttachDispatchAsync(id, dispatch.Id, ct).ConfigureAwait(false) ?? task;
        await EmitAttentionAsync(
            $"task:{task.Id}", task.CliName, AgentAttentionState.Working,
            AttentionSignalKind.Lifecycle, "task running", null, ct).ConfigureAwait(false);
        return task;
    }

    public async Task<DelegationTaskDto?> FinishRunAsync(
        string id, bool succeeded, string? detail, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var task = await _tasks.FinalizeAsync(
            id,
            succeeded ? DelegationTaskStatus.Done : DelegationTaskStatus.Failed,
            Truncate(detail),
            now,
            CancellationToken.None).ConfigureAwait(false);
        if (task is null)
        {
            return null;
        }

        // RF-002: settle the completion authority before the mailbox signal.
        var activeDispatch = task.ActiveDispatchId;
        if (activeDispatch is not null)
        {
            try
            {
                await _dispatches.TransitionAsync(
                    activeDispatch, DispatchState.Settled, null, now,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (DomainException)
            {
                // dispatch already terminal (abandoned/released) — finalize wins.
            }

            await _tasks.AttachDispatchAsync(task.Id, null, CancellationToken.None)
                .ConfigureAwait(false);
        }

        var kind = succeeded ? AgentMailboxKinds.WorkerDone : AgentMailboxKinds.Escalation;
        await _mailbox.AddAsync(
            new PostMailboxMessageRequest(
                task.Scope,
                task.CliName,
                "@all",
                $"task {task.Id} ({task.CliName}) {(succeeded ? "done" : "failed")}: {Truncate(detail, 500)}",
                kind,
                TaskId: task.Id,
                DispatchId: activeDispatch),
            CancellationToken.None).ConfigureAwait(false);
        await EmitAttentionAsync(
            $"task:{task.Id}", task.CliName,
            succeeded ? AgentAttentionState.Done : AgentAttentionState.Blocked,
            AttentionSignalKind.Lifecycle,
            succeeded ? "worker_done" : $"failed: {Truncate(detail, 200)}",
            null, CancellationToken.None).ConfigureAwait(false);
        return task;
    }

    public async Task<DelegationTaskDto?> MarkStaleAsync(string id, string reason, CancellationToken ct = default)
    {
        var task = await _tasks.FinalizeAsync(
            id, DelegationTaskStatus.Stale, reason,
            _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        if (task is not null)
        {
            await EmitAttentionAsync(
                $"task:{task.Id}", task.CliName, AgentAttentionState.Blocked,
                AttentionSignalKind.Lifecycle, $"stale: {Truncate(reason, 200)}",
                null, ct).ConfigureAwait(false);
        }

        return task;
    }

    public async Task<DelegationTaskDto?> CancelTaskAsync(string id, string reason, CancellationToken ct = default)
    {
        var task = await _tasks.FinalizeAsync(
            id, DelegationTaskStatus.Cancelled, reason,
            _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        if (task is not null)
        {
            await EmitAttentionAsync(
                $"task:{task.Id}", task.CliName, AgentAttentionState.Exited,
                AttentionSignalKind.Lifecycle, $"cancelled: {Truncate(reason, 200)}",
                null, ct).ConfigureAwait(false);
        }

        return task;
    }

    public Task HeartbeatAsync(string id, CancellationToken ct = default) =>
        _tasks.HeartbeatAsync(id, _time.GetUtcNow().UtcDateTime, ct);

    public async Task<MailboxMessageDto> PostAsync(
        PostMailboxMessageRequest request, CancellationToken ct = default)
    {
        var effective = request;
        // RF-009: lifecycle kinds without task identity never go to group
        // addresses (@all/@idle/@<cli>/@worktree:*). A worker_done/heartbeat
        // carrying taskId/dispatchId is a completion report — allowed.
        if (request.Kind is AgentMailboxKinds.WorkerDone or AgentMailboxKinds.Heartbeat
                or AgentMailboxKinds.Decision
            && request.ToAgent.StartsWith('@')
            && request.TaskId is null && request.DispatchId is null)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"lifecycle kind {request.Kind} cannot target group address {request.ToAgent}");
        }

        if (request.Kind is AgentMailboxKinds.WorkerDone or AgentMailboxKinds.Heartbeat)
        {
            // RF-002: completion authority = dispatchId + taskId juntos. Legacy
            // payloads without dispatchId get the task's active dispatch
            // attached (compat shim); a wrong/stale one is refused outright.
            if (request.DispatchId is { } dispatchId)
            {
                var dispatch = await _dispatches.GetAsync(dispatchId, ct).ConfigureAwait(false);
                var taskId = request.TaskId ?? dispatch?.TaskId;
                var task = taskId is null
                    ? null
                    : await _tasks.GetAsync(taskId, ct).ConfigureAwait(false);
                var valid = dispatch is not null && task is not null
                    && string.Equals(dispatch.TaskId, taskId, StringComparison.Ordinal)
                    && string.Equals(task.ActiveDispatchId, dispatch.Id, StringComparison.Ordinal);
                if (!valid)
                {
                    throw new DomainException(
                        TaskboardDomainErrorCodes.InvalidValue,
                        $"stale_dispatch: {dispatchId} is not the active dispatch of {taskId}");
                }

                effective = request with { TaskId = taskId };
            }
            else if (request.TaskId is { } legacyTaskId)
            {
                var task = await _tasks.GetAsync(legacyTaskId, ct).ConfigureAwait(false);
                effective = request with { DispatchId = task?.ActiveDispatchId };
            }
        }

        var message = await _mailbox.AddAsync(effective, ct).ConfigureAwait(false);
        if (string.Equals(message.Kind, AgentMailboxKinds.Escalation, StringComparison.Ordinal))
        {
            await EmitAttentionAsync(
                $"scope:{message.Scope}", message.FromAgent, AgentAttentionState.Blocked,
                AttentionSignalKind.Mailbox, "escalation",
                Truncate(message.Payload, 200), ct).ConfigureAwait(false);
        }

        return message;
    }

    public async Task<MailboxMessageDto> PostDecisionAsync(
        string scope, string fromAgent, string question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "question is required");
        }

        var message = await _mailbox.AddAsync(
            new PostMailboxMessageRequest(
                scope, fromAgent, "@all", question.Trim(), AgentMailboxKinds.Decision),
            ct).ConfigureAwait(false);
        await EmitAttentionAsync(
            $"scope:{message.Scope}", message.FromAgent, AgentAttentionState.WaitingYou,
            AttentionSignalKind.Mailbox, "decision",
            Truncate(question.Trim(), 200), ct).ConfigureAwait(false);
        return message;
    }

    public async Task<IReadOnlyList<MailboxMessageDto>> ReadInboxAsync(
        string scope, IReadOnlyCollection<string> recipients,
        int take = 50, bool unreadOnly = true, bool markRead = true, CancellationToken ct = default)
    {
        var messages = await _mailbox.ListForRecipientAsync(
            scope, recipients, take, unreadOnly, ct).ConfigureAwait(false);
        if (markRead && messages.Count > 0)
        {
            var unread = messages.Where(m => m.ReadAt is null).Select(m => m.Id).ToList();
            if (unread.Count > 0)
            {
                var now = _time.GetUtcNow().UtcDateTime;
                await _mailbox.MarkReadAsync(unread, now, ct).ConfigureAwait(false);
                messages = messages
                    .Select(m => unread.Contains(m.Id) ? m with { ReadAt = now } : m)
                    .ToList();
            }
        }

        return messages;
    }

    public async Task<MailboxMessageDto?> ReplyMailboxAsync(
        string scope, string messageId, string fromAgent, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "reply body is required");
        }

        var original = await _mailbox.GetAsync(messageId, ct).ConfigureAwait(false);
        if (original is null || !string.Equals(original.Scope, scope, StringComparison.Ordinal))
        {
            return null;
        }

        var reply = await _mailbox.AddAsync(
            new PostMailboxMessageRequest(
                scope, fromAgent, original.FromAgent,
                $"re:{original.Id} {body.Trim()}", AgentMailboxKinds.Text),
            ct).ConfigureAwait(false);
        await _mailbox.MarkReadAsync([original.Id], _time.GetUtcNow().UtcDateTime, ct)
            .ConfigureAwait(false);
        return reply;
    }

    public async Task<int> DismissMailboxAsync(
        string scope, IReadOnlyCollection<string> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        var existing = new List<string>();
        foreach (var id in ids)
        {
            var message = await _mailbox.GetAsync(id, ct).ConfigureAwait(false);
            if (message is not null && string.Equals(message.Scope, scope, StringComparison.Ordinal))
            {
                existing.Add(id);
            }
        }

        if (existing.Count == 0)
        {
            return 0;
        }

        await _mailbox.MarkReadAsync(existing, _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        return existing.Count;
    }

    public async Task<DeliveryCheckResult> CheckDeliveriesAsync(
        CheckDeliveriesRequest request, CancellationToken ct = default)
    {
        var scope = string.IsNullOrWhiteSpace(request.Scope) ? "harness" : request.Scope.Trim();

        if (request.All)
        {
            var run = await _runs.GetOpenByScopeAsync(scope, ct).ConfigureAwait(false);
            var deliveries = run is null
                ? []
                : await _deliveries.ListByRunAsync(run.Id, 100, ct).ConfigureAwait(false);
            return new DeliveryCheckResult(null, [], Deliveries: deliveries);
        }

        if (request.AckDeliveryId is { } ackId)
        {
            var acked = await _deliveries.AckAsync(
                ackId, _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
            if (acked is null)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"delivery_not_found: {ackId}");
            }
            // fall through — return the next unacked/pending batch
        }

        if (request.Peek)
        {
            var peekRun = await _runs.GetOpenByScopeAsync(scope, ct).ConfigureAwait(false);
            var peekUnacked = peekRun is null
                ? null
                : await _deliveries.GetOldestUnackedAsync(peekRun.Id, ct).ConfigureAwait(false);
            var preview = peekUnacked is not null
                ? await _mailbox.ListByIdsAsync(peekUnacked.MessageIds, ct).ConfigureAwait(false)
                : await UndeliveredAsync(scope, request, ct).ConfigureAwait(false);
            return new DeliveryCheckResult(peekUnacked, preview, preview.Count);
        }

        var ensuredRun = await EnsureRunAsync(scope, null, null, ct).ConfigureAwait(false);
        var deadline = request.Wait
            ? _time.GetUtcNow().UtcDateTime + (request.Timeout ?? DefaultCheckWaitTimeout)
            : (DateTime?)null;

        while (true)
        {
            var unacked = await _deliveries.GetOldestUnackedAsync(ensuredRun.Id, ct)
                .ConfigureAwait(false);
            if (unacked is not null)
            {
                var replayed = await _mailbox.ListByIdsAsync(unacked.MessageIds, ct)
                    .ConfigureAwait(false);
                return new DeliveryCheckResult(unacked, replayed, replayed.Count);
            }

            var pending = await UndeliveredAsync(scope, request, ct).ConfigureAwait(false);
            if (pending.Count > 0)
            {
                var batch = pending
                    .Take(DeliveryBatchMax)
                    .Select(m => m.Id)
                    .ToList();
                var delivery = await _deliveries.AddAsync(ensuredRun.Id, batch, ct)
                    .ConfigureAwait(false);
                await _mailbox.AssignDeliveryAsync(batch, delivery.Id, ct).ConfigureAwait(false);
                return new DeliveryCheckResult(
                    delivery, pending.Take(DeliveryBatchMax).ToList(), pending.Count);
            }

            if (deadline is null || _time.GetUtcNow().UtcDateTime >= deadline)
            {
                return new DeliveryCheckResult(null, [], 0, TimedOut: request.Wait);
            }

            await Task.Delay(CheckWaitPoll, ct).ConfigureAwait(false);
        }
    }

    private const int DeliveryBatchMax = 50;

    private async Task<IReadOnlyList<MailboxMessageDto>> UndeliveredAsync(
        string scope, CheckDeliveriesRequest request, CancellationToken ct)
    {
        var pending = await _mailbox.ListUndeliveredForRecipientAsync(
            scope, request.Recipients, DeliveryBatchMax + 1, request.Types, ct)
            .ConfigureAwait(false);
        return request.Types is { Count: > 0 } types
            ? pending.Where(m => types.Contains(m.Kind)).ToList()
            : pending;
    }

    // ---- RF-004: ask/reply ----

    private static readonly TimeSpan DefaultAskTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AskPoll = TimeSpan.FromSeconds(1);

    public async Task<PendingQuestionDto> AskQuestionAsync(
        AskQuestionRequest request, CancellationToken ct = default)
    {
        var scope = string.IsNullOrWhiteSpace(request.Scope) ? "harness" : request.Scope.Trim();

        PendingQuestionDto question;
        if (request.ResumeMessageId is { } resumeId)
        {
            var existing = await _questions.GetByMessageAsync(resumeId, ct).ConfigureAwait(false);
            if (existing is null || !string.Equals(existing.Scope, scope, StringComparison.Ordinal))
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"unknown question message: {resumeId}");
            }

            question = existing.Status switch
            {
                PendingQuestionStatus.Answered => existing,
                PendingQuestionStatus.Pending => existing,
                _ => await _questions.ResumeAsync(existing.Id, ct).ConfigureAwait(false) ?? existing,
            };
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue, "question is required");
            }

            var run = await EnsureRunAsync(scope, null, null, ct).ConfigureAwait(false);
            var message = await _mailbox.AddAsync(
                new PostMailboxMessageRequest(
                    scope, request.FromAgent, request.ToAgent ?? "@all",
                    Truncate(request.Question, DetailMaxLength), AgentMailboxKinds.Question),
                ct).ConfigureAwait(false);
            question = await _questions.AddAsync(
                scope, run.Id, message.Id, request.FromAgent,
                Truncate(request.Question, DetailMaxLength),
                request.Options is { Count: > 0 } opts ? JsonSerializer.Serialize(opts) : null,
                ct).ConfigureAwait(false);
        }

        if (question.Status is PendingQuestionStatus.Answered)
        {
            return question;
        }

        var deadline = _time.GetUtcNow().UtcDateTime + (request.Timeout ?? DefaultAskTimeout);
        while (true)
        {
            var current = await _questions.GetAsync(question.Id, ct).ConfigureAwait(false) ?? question;
            if (current.Status is PendingQuestionStatus.Answered)
            {
                return current;
            }

            if (_time.GetUtcNow().UtcDateTime >= deadline)
            {
                return await _questions.TimeOutAsync(question.Id, _time.GetUtcNow().UtcDateTime, ct)
                    .ConfigureAwait(false) ?? current;
            }

            await Task.Delay(AskPoll, ct).ConfigureAwait(false);
        }
    }

    public async Task<PendingQuestionDto> ReplyQuestionAsync(
        string scope, string messageId, string answer, string? fromAgent = null,
        CancellationToken ct = default)
    {
        var question = await _questions.GetByMessageAsync(messageId, ct).ConfigureAwait(false);
        if (question is null || !string.Equals(question.Scope, scope, StringComparison.Ordinal))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"unknown question message: {messageId}");
        }

        if (question.Status is not PendingQuestionStatus.Pending)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"question_not_pending: {question.Id} is {question.Status}");
        }

        var answered = await _questions.AnswerAsync(
            question.Id, answer, _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        await _mailbox.AddAsync(
            new PostMailboxMessageRequest(
                scope, fromAgent ?? "assistant", question.FromAgent,
                Truncate(answer, DetailMaxLength), AgentMailboxKinds.Answer),
            ct).ConfigureAwait(false);
        return answered ?? question with { Status = PendingQuestionStatus.Answered, Answer = answer };
    }

    // ---- RF-005: task-bound gates ----

    public async Task<DelegationGateDto> CreateGateAsync(
        string scope, string taskId, string question,
        IReadOnlyList<string>? options = null, CancellationToken ct = default)
    {
        var task = await _tasks.GetAsync(taskId, ct).ConfigureAwait(false);
        if (task is null || !string.Equals(task.Scope, scope, StringComparison.Ordinal))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, $"unknown task: {taskId}");
        }

        if (task.Status is DelegationTaskStatus.Done or DelegationTaskStatus.Failed
            or DelegationTaskStatus.Stale or DelegationTaskStatus.Cancelled)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"cannot gate a terminal task ({task.Status})");
        }

        var gate = await _gates.AddAsync(
            taskId, scope, question,
            options is { Count: > 0 } opts ? JsonSerializer.Serialize(opts) : null,
            ct).ConfigureAwait(false);
        await _tasks.MarkBlockedAsync(taskId, $"gate {gate.Id}: {question}", ct)
            .ConfigureAwait(false);
        return gate;
    }

    public async Task<DelegationGateDto> ResolveGateAsync(
        string scope, string gateId, string resolution, CancellationToken ct = default)
    {
        var gate = await _gates.GetAsync(gateId, ct).ConfigureAwait(false);
        if (gate is null || !string.Equals(gate.Scope, scope, StringComparison.Ordinal))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, $"unknown gate: {gateId}");
        }

        var resolved = await _gates.ResolveAsync(
            gateId, resolution, _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false)
            ?? gate;

        // Release the task only when no open sibling gates remain.
        if (!await _gates.HasOpenForTaskAsync(gate.TaskId, ct).ConfigureAwait(false))
        {
            await _tasks.UnblockAsync(gate.TaskId, ct).ConfigureAwait(false);
            await ResolveDependenciesAsync(ct).ConfigureAwait(false);
        }

        return resolved;
    }

    public async Task<IReadOnlyList<DelegationGateDto>> ListGatesAsync(
        string scope, string? taskId = null, CancellationToken ct = default) =>
        taskId is not null
            ? (await _gates.ListByTaskAsync(taskId, ct).ConfigureAwait(false))
                .Where(g => string.Equals(g.Scope, scope, StringComparison.Ordinal))
                .ToList()
            : await _gates.ListByScopeAsync(scope, ct).ConfigureAwait(false);

    public async Task<DelegationTaskDto?> BlockTaskAsync(
        string scope, string taskId, string? resultJson = null, CancellationToken ct = default)
    {
        var task = await _tasks.GetAsync(taskId, ct).ConfigureAwait(false);
        if (task is null || !string.Equals(task.Scope, scope, StringComparison.Ordinal))
        {
            return null;
        }

        return await _tasks.MarkBlockedAsync(
            taskId, Truncate(resultJson, DetailMaxLength), ct).ConfigureAwait(false);
    }

    // ---- SPEC-20261020-v2 RF-006: worker dispatch lifecycle ----

    public Task<DispatchDto?> GetDispatchAsync(string dispatchId, CancellationToken ct = default) =>
        _dispatches.GetAsync(dispatchId, ct);

    public async Task<string?> ReadDispatchOutputAsync(
        string dispatchId, int lineLimit = 200, CancellationToken ct = default)
    {
        var dispatch = await _dispatches.GetAsync(dispatchId, ct).ConfigureAwait(false);
        if (dispatch?.ArchivedOutput is null)
        {
            return null;
        }

        var lines = dispatch.ArchivedOutput.Split('\n');
        if (lineLimit <= 0 || lines.Length <= lineLimit)
        {
            return dispatch.ArchivedOutput;
        }

        return string.Join('\n', lines[^lineLimit..]);
    }

    public async Task<DispatchDto?> ReleaseDispatchAsync(
        string scope, string dispatchId, string? archivedOutput = null,
        CancellationToken ct = default) =>
        await TransitionDispatchForScopeAsync(
            scope, dispatchId, DispatchState.Released,
            archivedOutput is null ? null : Truncate(archivedOutput, DetailMaxLength),
            ct).ConfigureAwait(false);

    public async Task<DispatchDto?> RetainDispatchAsync(
        string scope, string dispatchId, CancellationToken ct = default) =>
        await TransitionDispatchForScopeAsync(
            scope, dispatchId, DispatchState.Retained, null, ct).ConfigureAwait(false);

    public async Task<DispatchDto?> AbandonDispatchAsync(
        string scope, string dispatchId, CancellationToken ct = default)
    {
        var transitioned = await TransitionDispatchForScopeAsync(
            scope, dispatchId, DispatchState.Abandoned, null, ct).ConfigureAwait(false);
        if (transitioned is not null)
        {
            await ClearActiveDispatchAsync(transitioned.TaskId, dispatchId, ct).ConfigureAwait(false);
        }

        return transitioned;
    }

    /// <summary>Scope-guarded dispatch transition + active-dispatch cleanup on release.</summary>
    private async Task<DispatchDto?> TransitionDispatchForScopeAsync(
        string scope, string dispatchId, DispatchState state, string? archivedOutput,
        CancellationToken ct)
    {
        var dispatch = await _dispatches.GetAsync(dispatchId, ct).ConfigureAwait(false);
        if (dispatch is null)
        {
            return null;
        }

        var run = await _runs.GetAsync(dispatch.RunId, ct).ConfigureAwait(false);
        if (run is null || !string.Equals(run.Scope, scope, StringComparison.Ordinal))
        {
            return null;
        }

        var transitioned = await _dispatches.TransitionAsync(
            dispatchId, state, archivedOutput, _time.GetUtcNow().UtcDateTime, ct)
            .ConfigureAwait(false);
        if (state is DispatchState.Released && transitioned is not null)
        {
            await ClearActiveDispatchAsync(transitioned.TaskId, dispatchId, ct).ConfigureAwait(false);
        }

        return transitioned;
    }

    private async Task ClearActiveDispatchAsync(
        string taskId, string dispatchId, CancellationToken ct)
    {
        var task = await _tasks.GetAsync(taskId, ct).ConfigureAwait(false);
        if (task?.ActiveDispatchId == dispatchId)
        {
            await _tasks.ClearActiveDispatchAsync(taskId, ct).ConfigureAwait(false);
        }
    }

    // ---- SPEC-20261020-v2 RF-010: delegate_reset ----

    public async Task<ResetRunResult> ResetRunAsync(
        string scope, bool tasks, bool messages, bool force = false, CancellationToken ct = default)
    {
        if (!tasks && !messages)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "nothing to reset — pass --tasks, --messages or --all");
        }

        var run = await _runs.GetOpenByScopeAsync(scope, ct).ConfigureAwait(false);

        if (run is not null && !force)
        {
            var open = await _dispatches.ListOpenByRunAsync(run.Id, ct).ConfigureAwait(false);
            if (open.Count > 0)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"run has {open.Count} open dispatch(es) — pass --force to reset anyway");
            }
        }

        var tasksDeleted = 0;
        var messagesDeleted = 0;
        if (tasks)
        {
            if (run is not null)
            {
                await _dispatches.DeleteByRunAsync(run.Id, ct).ConfigureAwait(false);
            }

            await _gates.DeleteByScopeAsync(scope, ct).ConfigureAwait(false);
            tasksDeleted = await _tasks.DeleteByScopeAsync(scope, ct).ConfigureAwait(false);
        }

        if (messages)
        {
            if (run is not null)
            {
                await _deliveries.DeleteByRunAsync(run.Id, ct).ConfigureAwait(false);
            }

            await _questions.DeleteByScopeAsync(scope, ct).ConfigureAwait(false);
            messagesDeleted = await _mailbox.DeleteByScopeAsync(scope, ct).ConfigureAwait(false);
        }

        return new ResetRunResult(tasksDeleted, messagesDeleted);
    }

    /// <summary>RF-002: preamble given to the worker — spec, ids, mailbox addr, contract.</summary>
    private static string BuildPreamble(
        DelegationTaskDto task, string dispatchId, string runId, int attempt) =>
        JsonSerializer.Serialize(new
        {
            taskId = task.Id,
            dispatchId,
            runId,
            attempt,
            cli = task.CliName,
            prompt = task.Prompt,
            sharedPathsApplied = task.SharedPathsApplied,
            // SPEC-20261021-worktree-setup-scripts RF-004: the worker knows
            // whether setup already ran (ok), is still running, or failed —
            // and gets the scrubbed log tail to act on it.
            setup = task.SetupState is null
                ? null
                : new { state = task.SetupState, logTail = task.SetupLogTail },
            // RF-007 cli-autonomy-profiles: the worker sees its own permission
            // envelope so it can plan inside it (resolved name, not raw null).
            autonomy = task.Autonomy ?? "edit",
            // RF-004 sdd-worker-brief: the worker brief variant follows the
            // task kind (implement|research|review) — specPath/acRefs arrive
            // with the task-prompt-refiner anchors.
            taskKind = task.Kind,
            mailbox = new { addr = task.Id, send = "agent_send", check = "agent_check" },
            contract = "report worker_done with taskId+dispatchId when finished; heartbeats keep the task alive",
        });

    private enum Verdict
    {
        StillPending,
        Ready,
        Cancel,
    }

    private static Verdict EvaluateDeps(
        DelegationTaskDto task, IReadOnlyDictionary<string, DelegationTaskDto> deps)
    {
        foreach (var depId in task.DependsOn)
        {
            if (!deps.TryGetValue(depId, out var dep))
            {
                return Verdict.Cancel;
            }

            if (dep.Status == DelegationTaskStatus.Done)
            {
                continue;
            }

            if (dep.Status is DelegationTaskStatus.Failed
                or DelegationTaskStatus.Stale or DelegationTaskStatus.Cancelled)
            {
                return Verdict.Cancel;
            }

            return Verdict.StillPending;
        }

        return Verdict.Ready;
    }

    private static string CancelDetail(
        DelegationTaskDto task, IReadOnlyDictionary<string, DelegationTaskDto> deps)
    {
        foreach (var depId in task.DependsOn)
        {
            if (!deps.TryGetValue(depId, out _))
            {
                return $"dependency missing: {depId}";
            }

            if (deps[depId].Status is DelegationTaskStatus.Failed
                or DelegationTaskStatus.Stale or DelegationTaskStatus.Cancelled)
            {
                return $"dependency failed: {depId}";
            }
        }

        return "dependency failed";
    }

    private static string Truncate(string? text, int max = DetailMaxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= max ? text : text[..max] + "…";
    }
}
