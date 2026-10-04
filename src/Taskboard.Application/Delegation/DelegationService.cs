using Taskboard;
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

    private readonly IDelegationTaskRepository _tasks;
    private readonly IAgentMailboxRepository _mailbox;
    private readonly TimeProvider _time;

    public DelegationService(
        IDelegationTaskRepository tasks,
        IAgentMailboxRepository mailbox,
        TimeProvider? timeProvider = null)
    {
        _tasks = tasks;
        _mailbox = mailbox;
        _time = timeProvider ?? TimeProvider.System;
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
            var retryTargets = await _tasks.ListByIdsAsync([retryOf], scope, ct).ConfigureAwait(false);
            var target = retryTargets.FirstOrDefault();
            if (target is null)
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"unknown retry_of: {retryOf}");
            }

            if (target.Status is not (DelegationTaskStatus.Failed
                or DelegationTaskStatus.Stale or DelegationTaskStatus.Cancelled))
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"retry_of target is not retryable (status {target.Status})");
            }
        }

        return await _tasks.AddAsync(request with { Scope = scope }, ct).ConfigureAwait(false);
    }

    public Task<DelegationTaskDto?> AttachWorktreeAsync(
        string taskId, string worktreeRunId, CancellationToken ct = default) =>
        _tasks.AttachWorktreeAsync(taskId, worktreeRunId, ct);

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

    public Task<DelegationTaskDto?> BeginRunAsync(string id, DateTime now, CancellationToken ct = default) =>
        _tasks.MarkRunningAsync(id, now, ct);

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

        var kind = succeeded ? AgentMailboxKinds.WorkerDone : AgentMailboxKinds.Escalation;
        await _mailbox.AddAsync(
            new PostMailboxMessageRequest(
                task.Scope,
                task.CliName,
                "@all",
                $"task {task.Id} ({task.CliName}) {(succeeded ? "done" : "failed")}: {Truncate(detail, 500)}",
                kind),
            CancellationToken.None).ConfigureAwait(false);
        return task;
    }

    public Task<DelegationTaskDto?> MarkStaleAsync(string id, string reason, CancellationToken ct = default) =>
        _tasks.FinalizeAsync(
            id, DelegationTaskStatus.Stale, reason,
            _time.GetUtcNow().UtcDateTime, ct);

    public Task<DelegationTaskDto?> CancelTaskAsync(string id, string reason, CancellationToken ct = default) =>
        _tasks.FinalizeAsync(
            id, DelegationTaskStatus.Cancelled, reason,
            _time.GetUtcNow().UtcDateTime, ct);

    public Task HeartbeatAsync(string id, CancellationToken ct = default) =>
        _tasks.HeartbeatAsync(id, _time.GetUtcNow().UtcDateTime, ct);

    public Task<MailboxMessageDto> PostAsync(PostMailboxMessageRequest request, CancellationToken ct = default) =>
        _mailbox.AddAsync(request, ct);

    public Task<MailboxMessageDto> PostDecisionAsync(
        string scope, string fromAgent, string question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "question is required");
        }

        return _mailbox.AddAsync(
            new PostMailboxMessageRequest(
                scope, fromAgent, "@all", question.Trim(), AgentMailboxKinds.Decision),
            ct);
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
