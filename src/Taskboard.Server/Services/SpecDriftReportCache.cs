using Taskboard.Dtos;

namespace Taskboard.Server.Services;

/// <summary>
/// Last drift report built by <see cref="SpecDriftScanService"/> — lets
/// <c>/api/specs/drift-report</c> answer without rescanning ~100 spec files
/// per request (SPEC-20260920-harness-maintenance-jobs RF-003).
/// </summary>
public sealed class SpecDriftReportCache
{
    private readonly object _gate = new();
    private SpecDriftReportDto? _last;

    public SpecDriftReportDto? Last
    {
        get { lock (_gate) { return _last; } }
    }

    public void Update(SpecDriftReportDto report)
    {
        lock (_gate)
        {
            _last = report;
        }
    }
}
