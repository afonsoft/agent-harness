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

    /// <inheritdoc/>
    public string? NormalizeWorkspacePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var expanded = WorkspacePaths.ExpandHome(path.Trim(), _homeDirectory);
        var full = Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(EnsureRoot(), expanded));
        // Symlinks resolve to their real target before the $HOME check — a
        // link under ~/repos pointing outside stays rejected (SPEC edge case).
        var real = ResolveRealPath(full);
        return WorkspacePaths.IsUnder(_homeDirectory, real) ? real : null;
    }

    /// <summary>
    /// Resolves every existing path segment through its link target —
    /// <c>Path.GetFullPath</c> is lexical and misses symlinked components.
    /// Nonexistent tails stay lexical.
    /// </summary>
    private static string ResolveRealPath(string full)
    {
        var root = Path.GetPathRoot(full);
        if (root is null)
        {
            return full;
        }

        var resolved = root;
        foreach (var segment in full[root.Length..]
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(resolved, segment);
            try
            {
                resolved = Directory.Exists(candidate)
                    ? new DirectoryInfo(candidate).ResolveLinkTarget(returnFinalTarget: true)?.FullName
                      ?? candidate
                    : candidate;
            }
            catch (Exception)
            {
                resolved = candidate;
            }
        }

        return Path.GetFullPath(resolved);
    }

    private const int MaxListedSubdirs = 200;

    /// <inheritdoc/>
    public WorkspaceDirsDto? ListSubdirs(string? path)
    {
        var full = string.IsNullOrWhiteSpace(path)
            ? EnsureRoot()
            : NormalizeWorkspacePath(path);
        if (full is null || !WorkspacePaths.IsUnder(_homeDirectory, full) || !Directory.Exists(full))
        {
            return null;
        }

        var parent = Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar));
        var parentListed = parent is not null && WorkspacePaths.IsUnder(_homeDirectory, parent)
            ? parent
            : null;

        var entries = new List<WorkspaceDirEntry>();
        var truncated = false;
        foreach (var dir in Directory.EnumerateDirectories(full).OrderBy(d => d, StringComparer.Ordinal))
        {
            var info = new DirectoryInfo(dir);
            if (info.Name.StartsWith(".", StringComparison.Ordinal) || info.LinkTarget is not null)
            {
                continue;
            }

            if (entries.Count >= MaxListedSubdirs)
            {
                truncated = true;
                break;
            }

            entries.Add(new WorkspaceDirEntry(info.Name, info.FullName));
        }

        return new WorkspaceDirsDto(full, parentListed, entries, truncated);
    }
}
