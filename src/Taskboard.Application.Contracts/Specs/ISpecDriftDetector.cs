using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Specs;

/// <summary>
/// Detects spec drift — divergence between a spec's declared status and the
/// repository reality (SPEC-20260919-ade-living-specs RF-002).
/// </summary>
public interface ISpecDriftDetector
{
    /// <param name="repo">Optional <c>owner/name</c> — scans
    /// <c>~/repos/&lt;name&gt;/.specs</c> on demand; absent → the configured
    /// default specs dir (SPEC-20260920 RF-005).</param>
    Task<SpecDriftReportDto> BuildReportAsync(string? repo = null, CancellationToken cancellationToken = default);
}
