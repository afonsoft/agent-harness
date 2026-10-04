namespace Taskboard.Application.Contracts.Workspace;

/// <summary>
/// Resolves repository workdirs confined to the workspace root
/// (SPEC-20260920-global-repo-selector RF-005/RF-006). Implemented by
/// <c>Taskboard.Integrations.Workspace.WorkspaceService</c>.
/// </summary>
/// <summary>One subdirectory entry of a workspace path.</summary>
public sealed record WorkspaceDirEntry(string Name, string Path);

/// <summary>
/// Directory listing for the workspace picker (SPEC-20261004 RF-001):
/// resolved path, parent (null at/over the jail boundary), subdirectories.
/// </summary>
public sealed record WorkspaceDirsDto(
    string Path,
    string? Parent,
    IReadOnlyList<WorkspaceDirEntry> Entries,
    bool Truncated);

public interface IWorkspacePathResolver
{
    /// <summary>
    /// <c>&lt;root&gt;/&lt;repo-name&gt;</c> when the clone exists, otherwise the
    /// workspace root itself with <paramref name="exists"/> = false.
    /// </summary>
    string ResolveCardWorkdir(string? repositoryFullName, out bool exists);

    /// <summary>
    /// SPEC-20261004 RF-002: normalizes a user-picked workspace path —
    /// <c>~</c>-expansion, relative paths resolved against the root. Returns
    /// the full path when it lives under <c>$HOME</c>, <c>null</c> otherwise
    /// (callers fall back to the default <c>~/repos</c> resolution).
    /// </summary>
    string? NormalizeWorkspacePath(string? path);

    /// <summary>
    /// SPEC-20261004 RF-001: lists subdirectories of <paramref name="path"/>
    /// (default: the workspace root), confined to <c>$HOME</c>. Returns
    /// <c>null</c> when the path is outside the home jail or not a directory.
    /// </summary>
    WorkspaceDirsDto? ListSubdirs(string? path);
}
