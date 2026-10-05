namespace Taskboard.ValueObjects;

/// <summary>Identifier of a provider-chat run (SPEC-20261005-chat-background-resume).</summary>
public sealed record ChatRunId : StringIdBase
{
    public ChatRunId(string value)
        : base(value)
    {
    }

    public static ChatRunId From(string value) => new(value);

    public static ChatRunId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
