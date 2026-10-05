namespace Taskboard.ValueObjects;

/// <summary>Identifier of a chat background job (SPEC-20261005-chat-jobs-schedule-search).</summary>
public sealed record ChatJobId : StringIdBase
{
    public ChatJobId(string value)
        : base(value)
    {
    }

    public static ChatJobId From(string value) => new(value);

    public static ChatJobId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
