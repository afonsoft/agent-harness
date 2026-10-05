using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>
/// A composer attachment (SPEC-20261005-chat-attachments-feedback RF-001).
/// Rows are <em>staged</em> (<see cref="MessageId"/> null) at upload and bound
/// to a message on send — unbound rows older than the retention window are
/// swept. Bytes live under <c>&lt;dataDir&gt;/attachments</c>, addressed by
/// the row id; the transcript carries descriptor lines only (RNF-001).
/// </summary>
public sealed class ChatAttachment : Entity<ChatAttachmentId>
{
    public ChatConversationId ConversationId { get; private set; } = default!;

    /// <summary>Null while staged — set when the send binds it (RF-001).</summary>
    public ChatMessageId? MessageId { get; private set; }

    public string FileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long ByteSize { get; private set; }

    /// <summary>Storage-relative path (<c>{id}.{ext}</c>) under the attachments dir.</summary>
    public string StoragePath { get; private set; } = default!;

    public string Sha256 { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }
    public DateTime? BoundAt { get; private set; }

    private ChatAttachment()
    {
    }

    private ChatAttachment(
        ChatAttachmentId id, ChatConversationId conversationId, string fileName,
        string contentType, long byteSize, string storagePath, string sha256, DateTime createdAt)
        : base(id)
    {
        ConversationId = conversationId;
        FileName = fileName;
        ContentType = contentType;
        ByteSize = byteSize;
        StoragePath = storagePath;
        Sha256 = sha256;
        CreatedAt = createdAt;
    }

    public static ChatAttachment Create(
        ChatAttachmentId id, ChatConversationId conversationId, string fileName,
        string contentType, long byteSize, string storagePath, string sha256, DateTime? now = null) =>
        new(id, conversationId, fileName, contentType, byteSize, storagePath, sha256, now ?? DateTime.UtcNow);

    /// <summary>Binds the staged row to the message it was sent with. Idempotent.</summary>
    public void Bind(ChatMessageId messageId, DateTime? now = null)
    {
        if (MessageId is not null)
        {
            return;
        }

        MessageId = messageId;
        BoundAt = now ?? DateTime.UtcNow;
    }

    /// <summary>Points a fork copy at the copied message (open question #2 — storage is shared).</summary>
    public void RebindTo(ChatMessageId messageId) => MessageId = messageId;
}
