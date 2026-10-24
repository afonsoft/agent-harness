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

    Task<DelegationTaskDto?> AttachWorktreeAsync(string taskId, string worktreeRunId, IReadOnlyList<string>? sharedPathsApplied = null, string? setupState = null, string? setupLogTail = null, CancellationToken ct = default);

    Task<IReadOnlyList<DelegationTaskDto>> ListTasksAsync(string scope, int take = 50, CancellationToken ct = default);
    Task<IReadOnlyList<DelegationTaskDto>> ListOpenAsync(CancellationToken ct = default);

    /// <summary>pending → ready when all deps are done; pending → cancelled on a missing/terminal-failed dep.</summary>
    Task<int> ResolveDependenciesAsync(CancellationToken ct = default);

    /// <summary>running tasks with heartbeat older than <paramref name="timeout"/> → failed + escalation.</summary>
    Task<int> SweepStaleHeartbeatsAsync(TimeSpan timeout, DateTime now, CancellationToken ct = default);

    /// <summary>ready → running (stamps start + heartbeat).</summary>
    Task<DelegationTaskDto?> BeginRunAsync(string id, DateTime now, CancellationToken ct = default);

    /// <summary>running → done/failed + posts worker_done/escalation to the scope mailbox.
    /// <paramref name="schemaValid"/> annotates the output_schema verdict (RF-G01;
    /// <c>false</c> keeps the child output intact).</summary>
    Task<DelegationTaskDto?> FinishRunAsync(
        string id, bool succeeded, string? detail,
        bool? schemaValid = null, CancellationToken ct = default);

    /// <summary>ready → stale (stale-base guard).</summary>
    Task<DelegationTaskDto?> MarkStaleAsync(string id, string reason, CancellationToken ct = default);

    /// <summary>any state → cancelled (worktree create failed, user cancel).</summary>
    Task<DelegationTaskDto?> CancelTaskAsync(string id, string reason, CancellationToken ct = default);

    Task HeartbeatAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// SPEC-20261020-v2 RF-001: the open run of a scope — creates one when
    /// missing. Every delegate_task/plan/fanout belongs to it.
    /// </summary>
    Task<DelegationRunDto> EnsureRunAsync(
        string scope, string? objective = null, string? coordinatorKey = null,
        CancellationToken ct = default);

    /// <summary>
    /// SPEC-20261020-v2 RF-001: run overview — objective, its tasks, open
    /// dispatches and unacked deliveries. Null when the run doesn't exist.
    /// </summary>
    Task<DelegationRunShowDto?> GetRunShowAsync(
        string? runId, string scope, CancellationToken ct = default);

    /// <summary>
    /// SPEC-20261020-v2 RF-007: retry — <paramref name="retryOf"/> may point at
    /// a dispatch id or (compat) a task id. Reactivates the same task as
    /// pending and registers a new pending dispatch with attempt+1.
    /// </summary>
    Task<RetryTaskResult> RetryTaskAsync(
        string scope, string retryOf, string? refineOverride = null,
        CancellationToken ct = default);

    /// <summary>
    /// SPEC-20261020-v2 RF-003: FIFO delivery check — without
    /// <c>AckDeliveryId</c> returns the oldest unacked delivery (replayed);
    /// with it, acks and moves to the next. <c>Peek</c>/<c>All</c> never
    /// consume; <c>Wait</c> polls until the timeout.
    /// </summary>
    Task<DeliveryCheckResult> CheckDeliveriesAsync(
        CheckDeliveriesRequest request, CancellationToken ct = default);

    /// <summary>
    /// SPEC-20261020-v2 RF-004: blocking worker question — posts a
    /// <c>question</c> mailbox message + pending-question row and polls until
    /// answered or <paramref name="request"/>.Timeout elapses (→ timed_out,
    /// resumable via <c>ResumeMessageId</c> re-blocking the same message).
    /// </summary>
    Task<PendingQuestionDto> AskQuestionAsync(
        AskQuestionRequest request, CancellationToken ct = default);

    /// <summary>
    /// SPEC-20261020-v2 RF-004: coordinator answer — posts an <c>answer</c>
    /// message to the asker and resolves the question. Throws
    /// <c>question_not_pending</c> when the question is already resolved.
    /// </summary>
    Task<PendingQuestionDto> ReplyQuestionAsync(
        string scope, string messageId, string answer, string? fromAgent = null,
        CancellationToken ct = default);

    /// <summary>
    /// SPEC-20261020-v2 RF-005: bind a gate to a task — the task is held
    /// <c>blocked</c> until <see cref="ResolveGateAsync"/>.
    /// </summary>
    Task<DelegationGateDto> CreateGateAsync(
        string scope, string taskId, string question,
        IReadOnlyList<string>? options = null, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-005: resolve + release the task back to the DAG verdict.</summary>
    Task<DelegationGateDto> ResolveGateAsync(
        string scope, string gateId, string resolution, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-005: gates of a scope (optionally task-bound).</summary>
    Task<IReadOnlyList<DelegationGateDto>> ListGatesAsync(
        string scope, string? taskId = null, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-010: external block with a structured json reason.</summary>
    Task<DelegationTaskDto?> BlockTaskAsync(
        string scope, string taskId, string? resultJson = null, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-006: dispatch inspection — state, attempt, preamble, terminal.</summary>
    Task<DispatchDto?> GetDispatchAsync(string dispatchId, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-006: archived worker output (last <paramref name="lineLimit"/> lines).</summary>
    Task<string?> ReadDispatchOutputAsync(
        string dispatchId, int lineLimit = 200, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-006: settled|retained → released (archive output, free worker).</summary>
    Task<DispatchDto?> ReleaseDispatchAsync(
        string scope, string dispatchId, string? archivedOutput = null, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-006: settled → retained (kept alive for follow-up).</summary>
    Task<DispatchDto?> RetainDispatchAsync(
        string scope, string dispatchId, CancellationToken ct = default);

    /// <summary>SPEC-20261020-v2 RF-006: non-released → abandoned; clears the task's active dispatch.</summary>
    Task<DispatchDto?> AbandonDispatchAsync(
        string scope, string dispatchId, CancellationToken ct = default);

    /// <summary>
    /// SPEC-20261020-v2 RF-010: clears run state — tasks+dispatches+gates
    /// and/or messages+deliveries+questions of the scope. Refuses while open
    /// dispatches exist unless <paramref name="force"/>.
    /// </summary>
    Task<ResetRunResult> ResetRunAsync(
        string scope, bool tasks, bool messages, bool force = false, CancellationToken ct = default);

    Task<MailboxMessageDto> PostAsync(PostMailboxMessageRequest request, CancellationToken ct = default);

    /// <summary>Posts a system <c>decision</c> message to <c>@all</c> — the decision
    /// gate (SPEC-20261006 RF-005): the coordinator asks, humans answer via mailbox.</summary>
    Task<MailboxMessageDto> PostDecisionAsync(
        string scope, string fromAgent, string question, CancellationToken ct = default);

    /// <summary>Inbox read; <paramref name="markRead"/> stamps ReadAt on the returned unread ones.</summary>
    Task<IReadOnlyList<MailboxMessageDto>> ReadInboxAsync(
        string scope, IReadOnlyCollection<string> recipients,
        int take = 50, bool unreadOnly = true, bool markRead = true, CancellationToken ct = default);

    /// <summary>
    /// SPEC-20261007 RF-001: human reply — posts a <c>text</c> message back to
    /// the original sender and marks the original read (resolves the
    /// escalation/decision). Returns null when the message is unknown or
    /// belongs to another scope.
    /// </summary>
    Task<MailboxMessageDto?> ReplyMailboxAsync(
        string scope, string messageId, string fromAgent, string body, CancellationToken ct = default);

    /// <summary>SPEC-20261007 RF-001: marks mailbox messages read without replying.</summary>
    Task<int> DismissMailboxAsync(
        string scope, IReadOnlyCollection<string> ids, CancellationToken ct = default);
}
