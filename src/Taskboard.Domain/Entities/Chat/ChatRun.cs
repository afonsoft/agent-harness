using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>
/// One provider-chat turn executed detached from the HTTP request
/// (SPEC-20261005-chat-background-resume RF-001). The run owns the tool loop;
/// <see cref="PartialContent"/>/<see cref="PartialReasoning"/> are throttled
/// checkpoints of the in-flight assistant text so a client attaching later
/// resumes at the last flushed point.
/// </summary>
public sealed class ChatRun : AggregateRoot<ChatRunId>
{
    public ChatConversationId ConversationId { get; private set; } = default!;

    /// <summary>The user message that opened the run — the transcript cutoff.</summary>
    public ChatMessageId TriggerMessageId { get; private set; } = default!;

    public ChatRunStatus Status { get; private set; } = ChatRunStatus.Queued;

    public string? PartialContent { get; private set; }

    public string? PartialReasoning { get; private set; }

    public string? Error { get; private set; }

    public int? TokensIn { get; private set; }

    public int? TokensOut { get; private set; }

    /// <summary>
    /// SPEC-20261005-chat-context-management RF-007: the effective wire-token
    /// budget the run compacted against (null = compaction disabled).
    /// </summary>
    public int? ContextTokensLimit { get; private set; }

    /// <summary>How many prune/summarize passes the run performed.</summary>
    public int CompactionCount { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? StartedAt { get; private set; }

    /// <summary>SPEC-20261012-chat-run-controls: when the run parked (null = not paused).</summary>
    public DateTime? PausedAt { get; private set; }

    public DateTime? FinishedAt { get; private set; }

    private ChatRun()
    {
    }

    private ChatRun(ChatRunId id, ChatConversationId conversationId, ChatMessageId triggerMessageId, DateTime createdAt)
        : base(id)
    {
        ConversationId = conversationId;
        TriggerMessageId = triggerMessageId;
        CreatedAt = createdAt;
    }

    public static ChatRun Create(
        ChatRunId id, ChatConversationId conversationId, ChatMessageId triggerMessageId, DateTime? now = null) =>
        new(id, conversationId, triggerMessageId, now ?? DateTime.UtcNow);

    /// <summary>queued → running. Throws when the run already left the queue.</summary>
    public void Start(DateTime? now = null)
    {
        if (Status != ChatRunStatus.Queued)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Run '{Id}' cannot start from status '{Status}'.");
        }

        Status = ChatRunStatus.Running;
        StartedAt = now ?? DateTime.UtcNow;
        IncrementVersion();
    }

    /// <summary>Checkpoint of the in-flight assistant text — no state change.</summary>
    public void Checkpoint(string? content, string? reasoning)
    {
        PartialContent = content;
        PartialReasoning = reasoning;
    }

    /// <summary>RF-007: recorded after each compaction pass.</summary>
    public void RecordContextStats(int limit, int compactionCount)
    {
        ContextTokensLimit = limit;
        CompactionCount = compactionCount;
    }

    public void Complete(int? tokensIn, int? tokensOut, DateTime? now = null)
    {
        EnsureActive(nameof(Complete));
        Status = ChatRunStatus.Completed;
        TokensIn = tokensIn;
        TokensOut = tokensOut;
        Finish(now);
    }

    public void Fail(string? error, DateTime? now = null)
    {
        EnsureActive(nameof(Fail));
        Status = ChatRunStatus.Failed;
        Error = error;
        Finish(now);
    }

    /// <summary>
    /// SPEC-20261012-chat-run-controls: park at a tool boundary (or while
    /// queued). Keeps the partial checkpoint — the pause is observable but
    /// never interrupts in-flight work.
    /// </summary>
    public void Pause(DateTime? now = null)
    {
        if (Status != ChatRunStatus.Queued && Status != ChatRunStatus.Running)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Run '{Id}' cannot pause from status '{Status}'.");
        }

        Status = ChatRunStatus.Paused;
        PausedAt = now ?? DateTime.UtcNow;
        IncrementVersion();
    }

    /// <summary>
    /// Resumes a parked run: un-started rows go back to <c>queued</c> (they
    /// still need the dispatcher's <see cref="Start"/>); a parked live run
    /// returns to <c>running</c>. Throws on any other status.
    /// </summary>
    public void Resume(DateTime? now = null)
    {
        if (Status != ChatRunStatus.Paused)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Run '{Id}' cannot resume from status '{Status}'.");
        }

        Status = StartedAt is null ? ChatRunStatus.Queued : ChatRunStatus.Running;
        PausedAt = null;
        IncrementVersion();
    }

    /// <summary>
    /// Post-restart resume of a run that was parked mid-flight: the executor
    /// is gone, so the row re-enters the queue and the turn is re-driven from
    /// the transcript. Distinct from <see cref="Resume"/> — the in-memory
    /// checkpoint cannot be continued after a restart.
    /// </summary>
    public void Requeue(DateTime? now = null)
    {
        if (Status != ChatRunStatus.Paused)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Run '{Id}' cannot requeue from status '{Status}'.");
        }

        Status = ChatRunStatus.Queued;
        PausedAt = null;
        IncrementVersion();
    }

    /// <summary>User-requested stop — legal from any active status.</summary>
    public void Stop(DateTime? now = null)
    {
        EnsureActive(nameof(Stop));
        Status = ChatRunStatus.Stopped;
        Error = "stopped by user";
        Finish(now);
    }

    /// <summary>Boot sweep — the process died while the run was live.</summary>
    public void Interrupt(DateTime? now = null)
    {
        EnsureActive(nameof(Interrupt));
        Status = ChatRunStatus.Interrupted;
        Error = "interrupted by server restart";
        Finish(now);
    }

    private void EnsureActive(string transition)
    {
        if (!Status.IsActive)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Run '{Id}' cannot {transition} from terminal status '{Status}'.");
        }
    }

    private void Finish(DateTime? now)
    {
        FinishedAt = now ?? DateTime.UtcNow;
        PausedAt = null;
        PartialContent = null;
        PartialReasoning = null;
        IncrementVersion();
    }
}
