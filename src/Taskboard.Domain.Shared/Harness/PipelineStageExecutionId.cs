using Taskboard.ValueObjects;

namespace Taskboard.Harness;

public sealed record PipelineStageExecutionId : StringIdBase
{
    public PipelineStageExecutionId(string value)
        : base(value)
    {
    }

    public static PipelineStageExecutionId From(string value) => new(value);

    public static PipelineStageExecutionId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
