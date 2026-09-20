namespace Taskboard.Application.Contracts.Workspace;

/// <summary>
/// Resolves repository workdirs confined to the workspace root
/// (SPEC-20260920-global-repo-selector RF-005/RF-006). Implemented by
/// <c>Taskboard.Integrations.Workspace.WorkspaceService</c>.
/// </summary>
public interface IWorkspacePathResolver
{
    /// <summary>
    /// <c>&lt;root&gt;/&lt;repo-name&gt;</c> when the clone exists, otherwise the
    /// workspace root itself with <paramref name="exists"/> = false.
    /// </summary>
    string ResolveCardWorkdir(string? repositoryFullName, out bool exists);
}
