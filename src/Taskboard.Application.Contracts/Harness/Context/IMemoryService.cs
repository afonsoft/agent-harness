using Taskboard.Dtos;
using Taskboard.Harness;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Cross-session memory: persists facts/decisions/lessons per repository and
/// retrieves the relevant ones for prompt injection (SPEC RF-004).
/// </summary>
public interface IMemoryService
{
    Task<ProjectMemoryItemDto> AddMemoryAsync(
        string repositoryFullName,
        string topic,
        string content,
        IReadOnlyList<string>? tags = null,
        MemoryType type = MemoryType.Fact,
        CancellationToken cancellationToken = default);

    /// <summary>Keyword/tag lexical search scoped to a repository.</summary>
    Task<IReadOnlyList<ProjectMemoryItemDto>> SearchAsync(
        string repositoryFullName,
        string query,
        int take = 10,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectMemoryItemDto>> ListAsync(
        string repositoryFullName,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
