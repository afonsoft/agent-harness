using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>
/// A mid-turn steering message queued against an active run
/// (SPEC-20261005-chat-fork-steering RF-005/RNF-002). Persisted rows — not an
/// in-memory inbox — so pending steers survive dispatcher crash + attach, and
/// <c>cancel</c> can index the unclaimed ones.
/// </summary>
public sealed class ChatSteer : Entity<ChatSteerId>
{
    /// <summary>RF-005 cap on pending (unclaimed) steers per run.</summary>
    public const int MaxPendingPerRun = 10;

    public ChatRunId RunId { get; private set; } = default!;
    public ChatConversationId ConversationId { get; private set; } = default!;
    public string Content { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }
    public DateTime? ClaimedAt { get; private set; }

    /// <summary>
    /// SPEC-20261005-chat-attachments-feedback: JSON list of staged attachment
    /// ids sent with the steer — bound to the steer message the drain persists.
    /// </summary>
    public string? AttachmentIdsJson { get; private set; }

    private ChatSteer()
    {
    }

    private ChatSteer(
        ChatSteerId id, ChatRunId runId, ChatConversationId conversationId,
        string content, DateTime createdAt, string? attachmentIdsJson)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Steer content cannot be empty.");
        }

        RunId = runId;
        ConversationId = conversationId;
        Content = content;
        CreatedAt = createdAt;
        AttachmentIdsJson = attachmentIdsJson;
    }

    public static ChatSteer Create(
        ChatSteerId id, ChatRunId runId, ChatConversationId conversationId,
        string content, DateTime? now = null, string? attachmentIdsJson = null) =>
        new(id, runId, conversationId, content, now ?? DateTime.UtcNow, attachmentIdsJson);

    /// <summary>
    /// Marks the steer as claimed by the executor — it lands on the wire at
    /// the next tool-result boundary (RF-006). Idempotent.
    /// </summary>
    public void Claim(DateTime? now = null)
    {
        ClaimedAt ??= now ?? DateTime.UtcNow;
    }
}
