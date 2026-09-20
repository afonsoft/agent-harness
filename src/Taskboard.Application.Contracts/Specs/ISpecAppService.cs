using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Specs;

/// <summary>Living-spec catalog operations (SPEC-20260919-ade-living-specs §5).</summary>
public interface ISpecAppService
{
    Task<IReadOnlyList<LivingSpecDto>> ListAsync(string? status, string? query, CancellationToken cancellationToken = default);

    Task<LivingSpecDetailDto?> GetAsync(string specId, CancellationToken cancellationToken = default);

    /// <summary>Rewrites the Status cell of the spec's metadata table in place; null when the spec does not exist.</summary>
    Task<LivingSpecDetailDto?> UpdateStatusAsync(string specId, string status, CancellationToken cancellationToken = default);
}
