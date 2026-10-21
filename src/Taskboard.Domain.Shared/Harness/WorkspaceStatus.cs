namespace Taskboard.Harness;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-006: human-facing cleanliness of a
/// worktree checkout, derived from <c>git status --porcelain</c> or set
/// explicitly via <c>worktree_set_status</c> / the card endpoint.
/// </summary>
public enum WorkspaceStatus
{
    /// <summary>Not yet probed or the probe failed.</summary>
    Unknown = 0,

    /// <summary>Porcelain output empty — no modifications.</summary>
    Clean,

    /// <summary>Uncommitted changes present.</summary>
    Dirty,

    /// <summary>Unmerged paths (UU/AA/…) — merge conflict in progress.</summary>
    Conflict,
}
