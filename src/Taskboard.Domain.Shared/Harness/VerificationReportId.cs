using Taskboard.ValueObjects;

namespace Taskboard.Harness;

public sealed record VerificationReportId : StringIdBase
{
    public VerificationReportId(string value)
        : base(value)
    {
    }

    public static VerificationReportId From(string value) => new(value);

    public static VerificationReportId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
