namespace Taskboard.Harness;

/// <summary>
/// Lifecycle status of a per-run Git worktree (SPEC-20260919-harness-workspace-isolation).
/// </summary>
public enum WorktreeStatus
{
    Active,
    Completed,
    Failed,
    RetainedForInspection,
    Removed
}
