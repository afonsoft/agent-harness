namespace Taskboard.Harness;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-001: how a gitignored path is
/// materialized inside a freshly created worktree.
/// </summary>
public static class WorktreeSharedModes
{
    /// <summary>Snapshot copy at provisioning time (default — isolated).</summary>
    public const string Copy = "copy";

    /// <summary>Relative symlink into the source repo — shared, opt-in.</summary>
    public const string Symlink = "symlink";

    /// <summary>Declared but never materialized (secrets opt-out).</summary>
    public const string Skip = "skip";

    public static bool IsValid(string mode) =>
        mode is Copy or Symlink or Skip;
}

/// <summary>Where a shared-path declaration came from (precedence order).</summary>
public static class WorktreeSharedSources
{
    /// <summary><c>Taskboard:Worktree:SharedPaths</c> config — highest.</summary>
    public const string Config = "config";

    /// <summary><c>.harness/worktree.yaml</c> in the repository.</summary>
    public const string RepoYaml = "repoYaml";

    /// <summary><c>.worktreeinclude</c> file (gitignore-style).</summary>
    public const string Include = "include";

    /// <summary>Convention detect — node_modules/.env/venv/bin/obj/.next.</summary>
    public const string Auto = "auto";
}
