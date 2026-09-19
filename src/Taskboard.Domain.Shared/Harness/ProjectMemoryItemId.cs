using Taskboard.ValueObjects;

namespace Taskboard.Harness;

public sealed record ProjectMemoryItemId : StringIdBase
{
    public ProjectMemoryItemId(string value)
        : base(value)
    {
    }

    public static ProjectMemoryItemId From(string value) => new(value);

    public static ProjectMemoryItemId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
