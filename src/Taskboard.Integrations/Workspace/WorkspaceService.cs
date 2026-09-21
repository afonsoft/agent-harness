using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Workspace;

namespace Taskboard.Integrations.Workspace;

/// <summary>
/// Resolves and guarantees the agent workspace root (SPEC-20260917-vscode-web-workspace
/// RF-001/RF-003): default <c>~/repos</c>, created on demand. All agent runs and
/// repository clones live under it.
/// </summary>
public sealed class WorkspaceService : IWorkspacePathResolver
{
    private readonly string _homeDirectory;
    private readonly ILogger<WorkspaceService> _logger;
    private readonly string _root;

    public WorkspaceService(string? configuredRoot, string homeDirectory, ILogger<WorkspaceService> logger)
    {
        _homeDirectory = homeDirectory;
        _logger = logger;
        _root = WorkspacePaths.ResolveRoot(configuredRoot, homeDirectory);
    }

    /// <summary>Workspace root path (absolute).</summary>
    public string Root => _root;

    /// <summary>Effective home directory.</summary>
    public string HomeDirectory => _homeDirectory;

    /// <summary>Ensures the root directory exists and returns it.</summary>
    public string EnsureRoot()
    {
        if (!Directory.Exists(_root))
        {
            _logger.LogInformation("Creating workspace root {Root}.", _root);
            Directory.CreateDirectory(_root);
        }

        return _root;
    }

    /// <summary>
    /// Expected workdir of a repository card — <c>&lt;root&gt;/&lt;repo&gt;</c>
    /// when it exists, otherwise the root itself.
    /// </summary>
    public string ResolveCardWorkdir(string? repositoryFullName, out bool exists)
    {
        var workdir = WorkspacePaths.RepoWorkdir(_root, repositoryFullName);
        exists = !string.Equals(workdir, _root, StringComparison.Ordinal) && Directory.Exists(workdir);
        return exists ? workdir : EnsureRoot();
    }

    /// <summary>
    /// Clamps <paramref name="path"/> to the effective home directory — anything
    /// outside <c>$HOME</c> falls back to the home dir (RF-006).
    /// </summary>
    public string ClampToHome(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !WorkspacePaths.IsUnder(_homeDirectory, path))
        {
            return _homeDirectory;
        }

        return Path.GetFullPath(path);
    }
}
