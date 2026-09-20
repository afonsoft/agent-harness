using Taskboard.ValueObjects;

namespace Taskboard.Harness;

public sealed record PipelineExecutionId : StringIdBase
{
    public PipelineExecutionId(string value)
        : base(value)
    {
    }

    public static PipelineExecutionId From(string value) => new(value);

    public static PipelineExecutionId NewGuid() => new($"pipe_{Guid.NewGuid():N}");
}
