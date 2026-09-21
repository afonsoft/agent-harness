using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Manages per-run isolated Git worktrees under <c>~/.taskboard/worktrees/{runId}</c>
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
