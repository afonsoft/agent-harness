using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Manages per-run isolated Git worktrees under <c>~/.agent-harness/worktrees/{runId}</c>
/// (SPEC-20260919-harness-workspace-isolation RF-001..RF-004).
/// </summary>
public interface IWorkspaceIsolationService
{
    Task<WorktreeSessionDto> CreateWorktreeAsync(
        string runId,
        string repositoryPath,
        string baseBranch,
        string taskSlug,
        bool retainOnFailure = false,
        CancellationToken cancellationToken = default);

    Task<WorktreeSessionDto?> GetAsync(string runId, CancellationToken cancellationToken = default);

    Task<WorkspaceDiffDto> GetDiffAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the children of <paramref name="subdir"/> (or the worktree root)
    /// — directories first, `.git` never returned, capped per directory
    /// (SPEC-20260921-cockpit-live-logs-explorer-diff RF-003). Returns
    /// <see langword="null"/> when the directory does not exist; throws
    /// <see cref="DomainException"/> when the path escapes the worktree.
    /// </summary>
    Task<WorktreeListDto?> ListFilesAsync(
        string runId,
        string? subdir,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a worktree file as text — capped in size, binary files return
    /// <see cref="WorktreeFileContentDto.Binary"/> with null content
    /// (RF-003). Returns <see langword="null"/> when the file does not exist;
    /// throws <see cref="DomainException"/> when the path escapes the worktree.
    /// </summary>
    Task<WorktreeFileContentDto?> ReadFileAsync(
        string runId,
        string path,
        CancellationToken cancellationToken = default);

    Task<string> CommitAsync(
        string runId,
        string message,
        string author,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes the worktree branch to `origin` (`git push -u origin &lt;branch&gt;`)
    /// — used by the cockpit "Create PR" action (SPEC-20260919-ade-cockpit-hitl
    /// RF-005). Returns the pushed branch name.
    /// </summary>
    Task<string> PushAsync(string runId, CancellationToken cancellationToken = default);

    Task RemoveWorktreeAsync(
        string runId,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>Marks the run's worktree as completed (kept for diff review).</summary>
    Task MarkCompletedAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>Marks the run's worktree as failed; honours <c>RetainOnFailure</c>.</summary>
    Task MarkFailedAsync(string runId, CancellationToken cancellationToken = default);
}
