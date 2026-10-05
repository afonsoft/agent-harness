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

    public DateTime CreatedAt { get; private set; }

    public DateTime? StartedAt { get; private set; }

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

    /// <summary>User-requested stop — legal from queued or running.</summary>
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
        PartialContent = null;
        PartialReasoning = null;
        IncrementVersion();
    }
}
