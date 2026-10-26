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

    /// <summary>→ done with summary (or failed/stale/cancelled with detail).
    /// <paramref name="schemaValid"/> annotates the output_schema verdict (RF-G01).</summary>
    Task<DelegationTaskDto?> FinalizeAsync(
        string id, DelegationTaskStatus terminal, string? detail, DateTime now,
        bool? schemaValid = null, CancellationToken ct = default);

    Task<DelegationTaskDto?> HeartbeatAsync(string id, DateTime now, CancellationToken ct = default);

    /// <summary>Attach the worktree run-id created by the fan-out tool.</summary>
    Task<DelegationTaskDto?> AttachWorktreeAsync(string id, string worktreeRunId, IReadOnlyList<string>? sharedPathsApplied = null, string? setupState = null, string? setupLogTail = null, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-002: point the task at its in-flight dispatch (null clears).</summary>
    Task<DelegationTaskDto?> AttachDispatchAsync(string id, string? dispatchId, CancellationToken ct = default);

    /// <summary>
    /// SPEC-20261020-v2 RF-007: failed/stale/cancelled → pending for a new
    /// dispatch attempt. <paramref name="model"/>, quando informado, troca o
    /// modelo de lançamento da nova tentativa (vazio/nulo mantém o anterior).
    /// </summary>
    Task<DelegationTaskDto?> ReactivateForRetryAsync(
        string id, string? model = null, CancellationToken ct = default);

    /// <summary>SPEC-20261022-task-prompt-refiner RF-005: persist the re-refined
    /// brief on retry (attempt context) — audit fields only, status untouched.</summary>
    Task<DelegationTaskDto?> ApplyRefinementAsync(
        string id, string refined, string mode, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-005/RF-010: non-terminal → blocked with a structured reason.</summary>
    Task<DelegationTaskDto?> MarkBlockedAsync(string id, string? reason, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-005: blocked → pending so the DAG verdict re-evaluates.</summary>
    Task<DelegationTaskDto?> UnblockAsync(string id, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-006: drops ActiveDispatchId when the active attempt is abandoned/released.</summary>
    Task<DelegationTaskDto?> ClearActiveDispatchAsync(string id, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-010: delegate_reset --tasks — returns the deleted count.</summary>
    Task<int> DeleteByScopeAsync(string scope, CancellationToken ct = default);
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

    /// <summary>
    /// SPEC-20261020-v2 RF-003: messages never handed out in a delivery
    /// (<c>DeliveryId IS NULL</c>) for the given recipients, oldest first;
    /// optionally restricted to the given kinds.
    /// </summary>
    Task<IReadOnlyList<MailboxMessageDto>> ListUndeliveredForRecipientAsync(
        string scope, IReadOnlyCollection<string> recipients, int take = 50,
        IReadOnlyCollection<string>? types = null, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-003: stamp a delivery batch on messages.</summary>
    Task AssignDeliveryAsync(
        IReadOnlyCollection<string> ids, string deliveryId, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-003: fetch messages by id preserving delivery order.</summary>
    Task<IReadOnlyList<MailboxMessageDto>> ListByIdsAsync(
        IReadOnlyCollection<string> ids, CancellationToken ct = default);

    Task MarkReadAsync(IReadOnlyCollection<string> ids, DateTime readAt, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-010: delegate_reset --messages — returns the deleted count.</summary>
    Task<int> DeleteByScopeAsync(string scope, CancellationToken ct = default);
}

/// <summary>SPEC-20261020-v2 RF-001: persistence of coordination runs.</summary>
public interface IDelegationRunRepository
{
    Task<DelegationRunDto?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>The open run of a scope — a scope has at most one.</summary>
    Task<DelegationRunDto?> GetOpenByScopeAsync(string scope, CancellationToken ct = default);

    Task<DelegationRunDto> AddAsync(
        string scope, string objective, string coordinatorKey, CancellationToken ct = default);

    Task<DelegationRunDto?> SettleAsync(string id, DateTime now, CancellationToken ct = default);
    Task<DelegationRunDto?> AbortAsync(string id, DateTime now, CancellationToken ct = default);
}

/// <summary>SPEC-20261020-v2 RF-002: persistence of dispatch attempts.</summary>
public interface IDispatchRepository
{
    Task<DispatchDto?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>All attempts of a task, oldest first.</summary>
    Task<IReadOnlyList<DispatchDto>> ListByTaskAsync(string taskId, CancellationToken ct = default);

    /// <summary>Non-terminal dispatches of a run (pending/dispatched).</summary>
    Task<IReadOnlyList<DispatchDto>> ListOpenByRunAsync(string runId, CancellationToken ct = default);

    /// <summary>SPEC-20261020-agent-hibernation RF-003: open dispatches bound to a
    /// terminal/session key (TerminalHandle) — a non-empty list is a hibernate guard.</summary>
    Task<IReadOnlyList<DispatchDto>> ListOpenForSessionAsync(string sessionKey, CancellationToken ct = default);

    /// <summary>Highest attempt number of a task — 0 when none.</summary>
    Task<int> LatestAttemptAsync(string taskId, CancellationToken ct = default);

    Task<DispatchDto> AddAsync(CreateDispatchRequest request, CancellationToken ct = default);

    /// <summary>Transition to <paramref name="state"/> via the entity guards.</summary>
    Task<DispatchDto?> TransitionAsync(
        string id, DispatchState state, string? archivedOutput, DateTime now,
        CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-010: delegate_reset --tasks — all dispatches of a run.</summary>
    Task<int> DeleteByRunAsync(string runId, CancellationToken ct = default);
}

/// <summary>SPEC-20261020-v2 RF-003: persistence of mailbox delivery batches.</summary>
public interface IDeliveryRepository
{
    Task<DeliveryDto?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>The oldest unacked delivery of a run — replay source.</summary>
    Task<DeliveryDto?> GetOldestUnackedAsync(string runId, CancellationToken ct = default);

    Task<IReadOnlyList<DeliveryDto>> ListByRunAsync(
        string runId, int take = 50, CancellationToken ct = default);

    Task<DeliveryDto> AddAsync(
        string runId, IReadOnlyList<string> messageIds, CancellationToken ct = default);

    Task<DeliveryDto?> AckAsync(string id, DateTime now, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-010: delegate_reset --messages — all deliveries of a run.</summary>
    Task<int> DeleteByRunAsync(string runId, CancellationToken ct = default);
}

/// <summary>SPEC-20261020-v2 RF-004: persistence of worker questions.</summary>
public interface IPendingQuestionRepository
{
    Task<PendingQuestionDto?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>The question carried by a mailbox message — ask/reply/resume join key.</summary>
    Task<PendingQuestionDto?> GetByMessageAsync(string messageId, CancellationToken ct = default);

    Task<PendingQuestionDto> AddAsync(
        string scope, string runId, string messageId, string fromAgent,
        string question, string? optionsJson, CancellationToken ct = default);

    /// <summary>pending → answered. Throws <c>question_not_pending</c> otherwise.</summary>
    Task<PendingQuestionDto?> AnswerAsync(
        string id, string answer, DateTime now, CancellationToken ct = default);

    /// <summary>pending → timed out (asker gave up this round).</summary>
    Task<PendingQuestionDto?> TimeOutAsync(string id, DateTime now, CancellationToken ct = default);

    /// <summary>timed-out → pending again for --resume; answered → question_not_pending.</summary>
    Task<PendingQuestionDto?> ResumeAsync(string id, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-010: delegate_reset --messages — all questions of a scope.</summary>
    Task<int> DeleteByScopeAsync(string scope, CancellationToken ct = default);
}

/// <summary>SPEC-20261020-v2 RF-005: persistence of task-bound gates.</summary>
public interface IDelegationGateRepository
{
    Task<DelegationGateDto?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<DelegationGateDto>> ListByTaskAsync(string taskId, CancellationToken ct = default);
    Task<IReadOnlyList<DelegationGateDto>> ListByScopeAsync(string scope, CancellationToken ct = default);
    Task<bool> HasOpenForTaskAsync(string taskId, CancellationToken ct = default);

    Task<DelegationGateDto> AddAsync(
        string taskId, string scope, string question, string? optionsJson,
        CancellationToken ct = default);

    /// <summary>open → resolved. Throws when already resolved.</summary>
    Task<DelegationGateDto?> ResolveAsync(
        string id, string resolution, DateTime now, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-010: delegate_reset --tasks — all gates of a scope.</summary>
    Task<int> DeleteByScopeAsync(string scope, CancellationToken ct = default);
}
