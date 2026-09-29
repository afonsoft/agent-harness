namespace Taskboard.ValueObjects;

public sealed record ChatConversationId : StringIdBase
{
    public ChatConversationId(string value)
        : base(value)
    {
    }

    public static ChatConversationId From(string value) => new(value);

    public static ChatConversationId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
