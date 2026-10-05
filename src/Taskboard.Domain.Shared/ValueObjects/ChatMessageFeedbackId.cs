namespace Taskboard.ValueObjects;

/// <summary>Identifier of a per-message feedback row (SPEC-20261005-chat-attachments-feedback).</summary>
public sealed record ChatMessageFeedbackId : StringIdBase
{
    public ChatMessageFeedbackId(string value)
        : base(value)
    {
    }

    public static ChatMessageFeedbackId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
