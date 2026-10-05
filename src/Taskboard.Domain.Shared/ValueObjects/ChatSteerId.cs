namespace Taskboard.ValueObjects;

/// <summary>Identifier of a mid-turn steer item (SPEC-20261005-chat-fork-steering).</summary>
public sealed record ChatSteerId : StringIdBase
{
    public ChatSteerId(string value)
        : base(value)
    {
    }

    public static ChatSteerId From(string value) => new(value);

    public static ChatSteerId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
