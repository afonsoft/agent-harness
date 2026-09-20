using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Specs;

/// <summary>Living-spec catalog operations (SPEC-20260919-ade-living-specs §5).</summary>
public interface ISpecAppService
{
    /// <param name="repo">Optional <c>owner/name</c> — reads <c>~/repos/&lt;name&gt;/.specs</c>;
    /// absent → the configured default specs dir (SPEC-20260920 RF-005).</param>
    Task<IReadOnlyList<LivingSpecDto>> ListAsync(
        string? status, string? query, string? repo = null, CancellationToken cancellationToken = default);

    Task<LivingSpecDetailDto?> GetAsync(string specId, string? repo = null, CancellationToken cancellationToken = default);

    /// <summary>Rewrites the Status cell of the spec's metadata table in place; null when the spec does not exist.</summary>
    Task<LivingSpecDetailDto?> UpdateStatusAsync(
        string specId, string status, string? repo = null, CancellationToken cancellationToken = default);
}
