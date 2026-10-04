using Taskboard.Delegation;
using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Delegation;

/// <summary>
/// SPEC-20261005: persistence of delegation DAG tasks. Entity state
/// transitions stay behind named methods so the DTO surface never leaks
/// domain objects across the Contracts boundary.
/// </summary>
public interface IDelegationTaskRepository
{
    Task<DelegationTaskDto?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>Recent tasks of one scope (conversation or "harness"), newest first.</summary>
    Task<IReadOnlyList<DelegationTaskDto>> ListByScopeAsync(string scope, int take = 50, CancellationToken ct = default);

    /// <summary>Tasks by id constrained to a scope — dependency/retry lookups.</summary>
    Task<IReadOnlyList<DelegationTaskDto>> ListByIdsAsync(
        IReadOnlyCollection<string> ids, string scope, CancellationToken ct = default);

    /// <summary>Non-terminal tasks the dispatcher must progress (any scope).</summary>
    Task<IReadOnlyList<DelegationTaskDto>> ListOpenAsync(CancellationToken ct = default);

    Task<DelegationTaskDto> AddAsync(CreateDelegationTaskRequest request, CancellationToken ct = default);

    /// <summary>pending → ready. No-op outside pending; returns the dto or null.</summary>
    Task<DelegationTaskDto?> MarkReadyAsync(string id, CancellationToken ct = default);

    /// <summary>→ running + started/heartbeat stamps.</summary>
    Task<DelegationTaskDto?> MarkRunningAsync(string id, DateTime now, CancellationToken ct = default);

    /// <summary>→ done with summary (or failed/stale/cancelled with detail).</summary>
    Task<DelegationTaskDto?> FinalizeAsync(
        string id, DelegationTaskStatus terminal, string? detail, DateTime now, CancellationToken ct = default);

    Task<DelegationTaskDto?> HeartbeatAsync(string id, DateTime now, CancellationToken ct = default);

    /// <summary>Attach the worktree run-id created by the fan-out tool.</summary>
    Task<DelegationTaskDto?> AttachWorktreeAsync(string id, string worktreeRunId, CancellationToken ct = default);
}

/// <summary>SPEC-20261005: persistence of agent mailbox messages.</summary>
public interface IAgentMailboxRepository
{
    Task<MailboxMessageDto> AddAsync(PostMailboxMessageRequest request, CancellationToken ct = default);

    /// <summary>SPEC-20261007 RF-001: single-message lookup for the reply path.</summary>
    Task<MailboxMessageDto?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Messages of <paramref name="scope"/> whose <c>ToAgent</c> is one of
    /// <paramref name="recipients"/>, oldest first.
    /// </summary>
    Task<IReadOnlyList<MailboxMessageDto>> ListForRecipientAsync(
        string scope, IReadOnlyCollection<string> recipients, int take = 50,
        bool unreadOnly = false, CancellationToken ct = default);

    /// <summary>SPEC-20261009 RF-004: all messages of a scope, newest first — event feed.</summary>
    Task<IReadOnlyList<MailboxMessageDto>> ListByScopeAsync(
        string scope, int take = 50, CancellationToken ct = default);

    Task MarkReadAsync(IReadOnlyCollection<string> ids, DateTime readAt, CancellationToken ct = default);
}
