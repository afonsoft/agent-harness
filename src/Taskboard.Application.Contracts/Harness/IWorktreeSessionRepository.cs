using Taskboard.Dtos;
using Taskboard.Harness;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Persistence for per-run worktree sessions — returns DTOs, never entities
/// (keeps <c>Integrations</c> decoupled from <c>Domain</c>).
/// </summary>
public interface IWorktreeSessionRepository
{
    Task<WorktreeSessionDto> CreateAsync(
        string runId,
        string repositoryPath,
        string baseBranch,
        string path,
        string branch,
        bool retainOnFailure,
        CancellationToken cancellationToken = default);

    Task<WorktreeSessionDto?> GetByRunIdAsync(string runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorktreeSessionDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<WorktreeSessionDto> RecordCommitAsync(string runId, string commitSha, CancellationToken cancellationToken = default);

    Task<WorktreeSessionDto> SetStatusAsync(string runId, WorktreeStatus status, CancellationToken cancellationToken = default);
}
