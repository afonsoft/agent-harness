using Taskboard.ValueObjects;

namespace Taskboard.Harness;

public sealed record WorktreeSessionId : StringIdBase
{
    public WorktreeSessionId(string value)
        : base(value)
    {
    }

    public static WorktreeSessionId From(string value) => new(value);

    public static WorktreeSessionId NewGuid() => new(Guid.NewGuid().ToString("N"));
}

/// <summary>Location tuple of a worktree session — the repository root it
/// belongs to, the base branch it forked from, and its own path/branch.</summary>
public sealed record WorktreeSessionPaths(
    string RepositoryPath,
    string BaseBranch,
    string Path,
    string Branch);
