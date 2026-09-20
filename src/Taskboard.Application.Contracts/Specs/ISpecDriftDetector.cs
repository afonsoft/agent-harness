using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Specs;

/// <summary>
/// Detects spec drift — divergence between a spec's declared status and the
/// repository reality (SPEC-20260919-ade-living-specs RF-002).
/// </summary>
public interface ISpecDriftDetector
{
    Task<SpecDriftReportDto> BuildReportAsync(CancellationToken cancellationToken = default);
}
