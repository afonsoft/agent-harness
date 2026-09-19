using Taskboard.ValueObjects;

namespace Taskboard.CliMetrics;

public sealed record CliMetricSourceId : StringIdBase
{
    public CliMetricSourceId(string value) : base(value) { }
    public static CliMetricSourceId From(string value) => new(value);
    public static CliMetricSourceId NewGuid() => new(Guid.NewGuid().ToString("N"));
}

public sealed record CliSessionMetricId : StringIdBase
{
    public CliSessionMetricId(string value) : base(value) { }
    public static CliSessionMetricId From(string value) => new(value);
    public static CliSessionMetricId NewGuid() => new(Guid.NewGuid().ToString("N"));
}

public sealed record CliDailyUsageAggregateId : StringIdBase
{
    public CliDailyUsageAggregateId(string value) : base(value) { }
    public static CliDailyUsageAggregateId From(string value) => new(value);
    public static CliDailyUsageAggregateId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
