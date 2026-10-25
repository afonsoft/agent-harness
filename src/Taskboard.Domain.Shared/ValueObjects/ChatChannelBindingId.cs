namespace Taskboard.ValueObjects;

public sealed record ChatChannelBindingId : StringIdBase
{
    public ChatChannelBindingId(string value)
        : base(value)
    {
    }

    public static ChatChannelBindingId From(string value) => new(value);

    public static ChatChannelBindingId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
