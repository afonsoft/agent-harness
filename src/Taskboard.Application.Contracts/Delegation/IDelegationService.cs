using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Delegation;

/// <summary>
/// SPEC-20261005: application service over the delegation DAG + mailbox.
/// Owns create-time validation (deps exist, retry targets, worktree
/// preconditions), the dependency resolution pass, heartbeat sweeps and the
/// finalize+mailbox coupling. The hosted dispatcher and the chat tools are
/// its only callers.
/// </summary>
public interface IDelegationService
{
    /// <summary>
    /// Validates and persists a new task. Throws <c>DomainException</c> when a
    /// dep id is unknown in the scope or <c>retry_of</c> points at a
    /// non-retryable/running task.
    /// </summary>
    Task<DelegationTaskDto> CreateTaskAsync(CreateDelegationTaskRequest request, CancellationToken ct = default);

    Task<DelegationTaskDto?> AttachWorktreeAsync(string taskId, string worktreeRunId, CancellationToken ct = default);

    Task<IReadOnlyList<DelegationTaskDto>> ListTasksAsync(string scope, int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<DelegationTaskDto>> ListOpenAsync(CancellationToken ct = default);

    /// <summary>pending → ready when all deps are done; pending → cancelled on a missing/terminal-failed dep.</summary>
    Task<int> ResolveDependenciesAsync(CancellationToken ct = default);

    /// <summary>running tasks with heartbeat older than <paramref name="timeout"/> → failed + escalation.</summary>
    Task<int> SweepStaleHeartbeatsAsync(TimeSpan timeout, DateTime now, CancellationToken ct = default);

    /// <summary>ready → running (stamps start + heartbeat).</summary>
    Task<DelegationTaskDto?> BeginRunAsync(string id, DateTime now, CancellationToken ct = default);

    /// <summary>running → done/failed + posts worker_done/escalation to the scope mailbox.</summary>
    Task<DelegationTaskDto?> FinishRunAsync(string id, bool succeeded, string? detail, CancellationToken ct = default);

    /// <summary>ready → stale (stale-base guard).</summary>
    Task<DelegationTaskDto?> MarkStaleAsync(string id, string reason, CancellationToken ct = default);

    /// <summary>any state → cancelled (worktree create failed, user cancel).</summary>
    Task<DelegationTaskDto?> CancelTaskAsync(string id, string reason, CancellationToken ct = default);

    Task HeartbeatAsync(string id, CancellationToken ct = default);

    Task<MailboxMessageDto> PostAsync(PostMailboxMessageRequest request, CancellationToken ct = default);

    /// <summary>Inbox read; <paramref name="markRead"/> stamps ReadAt on the returned unread ones.</summary>
    Task<IReadOnlyList<MailboxMessageDto>> ReadInboxAsync(
        string scope, IReadOnlyCollection<string> recipients,
        int take = 50, bool unreadOnly = true, bool markRead = true, CancellationToken ct = default);
}
