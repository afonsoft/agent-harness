using Taskboard.ValueObjects;

namespace Taskboard.Harness;

public sealed record WorktreeSessionId : StringIdBase
{
    public WorktreeSessionId(string value)
        : base(value)
    {
    }

    public static WorktreeSessionId From(string value) => new(value);

    public static WorktreeSessionId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
