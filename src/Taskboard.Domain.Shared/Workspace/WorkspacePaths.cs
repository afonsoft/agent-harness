using System.Text;

namespace Taskboard.Workspace;

/// <summary>
/// Pure path helpers for the agent workspace root (SPEC-20260917-vscode-web-workspace
/// RF-001/RF-003). All resolution is confined to the configured root — never
/// escapes via <c>..</c> or separators.
/// </summary>
public static class WorkspacePaths
{
    /// <summary>Default workspace root under $HOME when not configured.</summary>
    public const string DefaultRootName = "repos";

    /// <summary>Expands a leading <c>~</c>/<c>~/</c> to <paramref name="home"/>; other paths pass through.</summary>
    public static string ExpandHome(string path, string home)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path == "~")
        {
            return home;
        }

        return path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(home, path[2..])
            : path;
    }

    /// <summary>Resolves the workspace root: configured value (with ~ expansion) or <c>$HOME/repos</c>.</summary>
    public static string ResolveRoot(string? configured, string home)
    {
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(home, DefaultRootName)
            : ExpandHome(configured.Trim(), home);
        return Path.GetFullPath(root);
    }

    /// <summary>
    /// Last segment of <c>owner/name</c> sanitized to <c>[A-Za-z0-9._-]</c>
    /// (everything else becomes <c>-</c>); empty/invalid input → empty string.
    /// </summary>
    public static string SanitizeRepoName(string? repositoryFullName)
    {
        if (string.IsNullOrWhiteSpace(repositoryFullName))
        {
            return string.Empty;
        }

        var segment = repositoryFullName.Trim();
        var slash = segment.LastIndexOf('/');
        if (slash >= 0)
        {
            segment = segment[(slash + 1)..];
        }

        var builder = new StringBuilder(segment.Length);
        foreach (var c in segment)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-');
        }

        var name = builder.ToString().Trim('.');
        return name is "." or ".." ? string.Empty : name;
    }

    /// <summary>
    /// Workdir of a repository card: <c>&lt;root&gt;/&lt;name&gt;</c> — always
    /// inside <paramref name="root"/>; invalid names fall back to root itself.
    /// </summary>
    public static string RepoWorkdir(string root, string? repositoryFullName)
    {
        var name = SanitizeRepoName(repositoryFullName);
        return name.Length == 0 ? root : Path.Combine(root, name);
    }

    /// <summary>True when <paramref name="path"/> equals or is inside <paramref name="root"/>.</summary>
    public static bool IsUnder(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.Ordinal)
            || string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
    }
}
