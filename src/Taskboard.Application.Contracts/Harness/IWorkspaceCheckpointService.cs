namespace Taskboard.Application.Contracts.Harness;

/// <summary>One worktree checkpoint: a git commit on the worktree branch.</summary>
public sealed record WorktreeCheckpointDto(
    string Sha,
    string Label,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// SPEC-20261006 RF-004: commit-style checkpoints inside a managed worktree —
/// snapshot current state, list prior checkpoints, restore to one.
/// Keyed by <c>runId</c> so callers never touch raw paths.
/// </summary>
public interface IWorkspaceCheckpointService
{
    /// <summary>
    /// Commits every change in the run's worktree as a checkpoint
    /// (<c>harness-checkpoint:</c> prefix). A clean worktree returns a
    /// checkpoint of the current HEAD instead of failing.
    /// </summary>
    Task<WorktreeCheckpointDto> CreateCheckpointAsync(
        string runId, string? label = null, CancellationToken ct = default);

    /// <summary>Lists checkpoints newest-first (commits prefixed <c>harness-checkpoint:</c>).</summary>
    Task<IReadOnlyList<WorktreeCheckpointDto>> ListCheckpointsAsync(
        string runId, int take = 50, CancellationToken ct = default);

    /// <summary>Hard-resets the worktree to a checkpoint sha; unknown sha fails.</summary>
    Task<WorktreeCheckpointDto> RestoreCheckpointAsync(
        string runId, string sha, CancellationToken ct = default);
}
