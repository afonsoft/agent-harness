namespace Taskboard.ValueObjects;

/// <summary>Identifier of a run deliverable row (SPEC-20261005-chat-attachments-feedback).</summary>
public sealed record ChatRunDeliverableId : StringIdBase
{
    public ChatRunDeliverableId(string value)
        : base(value)
    {
    }

    public static ChatRunDeliverableId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
