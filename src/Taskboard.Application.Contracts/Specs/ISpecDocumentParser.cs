using Taskboard.Specs;

namespace Taskboard.Application.Contracts.Specs;

/// <summary>
/// Parses a <c>.specs/SPEC-*.md</c> markdown document into a
/// <see cref="LivingSpecification"/> projection. Never throws on malformed
/// input — anomalies surface as <see cref="LivingSpecification.Warnings"/>
/// (SPEC-20260919-ade-living-specs RF-001).
/// </summary>
public interface ISpecDocumentParser
{
    LivingSpecification Parse(string filePath, string markdown);
}
