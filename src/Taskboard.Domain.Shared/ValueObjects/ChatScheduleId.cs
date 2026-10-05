namespace Taskboard.ValueObjects;

/// <summary>Identifier of a chat scheduled follow-up (SPEC-20261005-chat-jobs-schedule-search).</summary>
public sealed record ChatScheduleId : StringIdBase
{
    public ChatScheduleId(string value)
        : base(value)
    {
    }

    public static ChatScheduleId From(string value) => new(value);

    public static ChatScheduleId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
