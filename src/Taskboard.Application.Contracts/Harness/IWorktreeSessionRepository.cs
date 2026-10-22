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
        WorktreeSessionPaths paths,
        bool retainOnFailure,
        CancellationToken cancellationToken = default,
        WorktreeProvisionOutcome? provisioning = null);

    Task<WorktreeSessionDto?> GetByRunIdAsync(string runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorktreeSessionDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<WorktreeSessionDto> RecordCommitAsync(string runId, string commitSha, CancellationToken cancellationToken = default);

    Task<WorktreeSessionDto> SetStatusAsync(string runId, WorktreeStatus status, CancellationToken cancellationToken = default);

    /// <summary>SPEC-20261021-worktree-setup-scripts P3: overwrite the
    /// setup lifecycle fields after a manual rerun.</summary>
    Task<WorktreeSessionDto> SetSetupAsync(
        string runId, string? state, string? logTail, CancellationToken cancellationToken = default);
}
