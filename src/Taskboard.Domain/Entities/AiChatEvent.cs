using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities;

public sealed class AiChatEvent : Entity<AiChatEventId>
{
    public AiChatThreadId ThreadId { get; private set; } = default!;
    public AiChatEventRole Role { get; private set; } = default!;
    public AiChatEventKind Kind { get; private set; } = AiChatEventKind.Message;
    public string Content { get; private set; } = default!;
    public string? PayloadJson { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private AiChatEvent()
    {
    }

    private AiChatEvent(
        AiChatEventId id,
        AiChatThreadId threadId,
        AiChatEventRole role,
        string content,
        DateTime createdAt,
        AiChatEventKind? kind = null,
        string? payloadJson = null)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content cannot be empty.", nameof(content));
        }

        ThreadId = threadId;
        Role = role;
        Content = content;
        CreatedAt = createdAt;
        Kind = kind ?? AiChatEventKind.Message;
        PayloadJson = payloadJson;
    }

    public static AiChatEvent Create(
        AiChatEventId id,
        AiChatThreadId threadId,
        AiChatEventRole role,
        string content,
        DateTime? now = null)
        => new(id, threadId, role, content, now ?? DateTime.UtcNow);

    public static AiChatEvent CreateTyped(
        AiChatEventId id,
        AiChatThreadId threadId,
        AiChatEventRole role,
        string content,
        AiChatEventKind kind,
        string? payloadJson = null,
        DateTime? now = null)
        => new(id, threadId, role, content, now ?? DateTime.UtcNow, kind, payloadJson);
}
