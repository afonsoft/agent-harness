namespace Taskboard.Application.Contracts.Skills;

public interface ISkillDiscoveryService
{
    Task<IReadOnlyList<SkillDto>> DiscoverAsync(CancellationToken cancellationToken = default);

    Task<SkillDetailDto?> GetDetailAsync(string source, string name, CancellationToken cancellationToken = default);

    Task<SkillFileResult> GetFileAsync(string source, string name, string relativePath, CancellationToken cancellationToken = default);
}
