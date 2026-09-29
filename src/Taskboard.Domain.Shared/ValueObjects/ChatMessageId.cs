namespace Taskboard.ValueObjects;

public sealed record ChatMessageId : StringIdBase
{
    public ChatMessageId(string value)
        : base(value)
    {
    }

    public static ChatMessageId From(string value) => new(value);

    public static ChatMessageId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
