namespace Taskboard.ValueObjects;

/// <summary>Identifier of a chat attachment (SPEC-20261005-chat-attachments-feedback).</summary>
public sealed record ChatAttachmentId : StringIdBase
{
    public ChatAttachmentId(string value)
        : base(value)
    {
    }

    public static ChatAttachmentId From(string value) => new(value);

    public static ChatAttachmentId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
