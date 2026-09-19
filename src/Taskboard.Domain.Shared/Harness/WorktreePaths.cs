using System.Text;
using Taskboard.Workspace;

namespace Taskboard.Harness;

/// <summary>
/// Pure path/name helpers for per-run Git worktrees
/// (SPEC-20260919-harness-workspace-isolation). Every session dir is confined
/// under <c>~/.taskboard/worktrees</c> — runIds/slugs are sanitized so they can
/// never escape via <c>..</c> or separators.
/// </summary>
public static class WorktreePaths
{
    /// <summary>Default worktree root: <c>&lt;home&gt;/.taskboard/worktrees</c>.</summary>
    public static string ResolveRoot(string home) => Path.Combine(home, ".taskboard", "worktrees");

    /// <summary>Session directory <c>&lt;root&gt;/&lt;sanitized-runId&gt;</c> — always inside root.</summary>
    public static string SessionDir(string root, string runId) => Path.Combine(root, Sanitize(runId));

    /// <summary>True when <paramref name="path"/> equals or is inside <paramref name="root"/>.</summary>
    public static bool IsUnder(string root, string path) => WorkspacePaths.IsUnder(root, path);

    /// <summary>
    /// Keeps ASCII letters/digits/<c>._-</c>, replaces everything else with <c>-</c>.
    /// Never returns a traversal segment.
    /// </summary>
    public static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "run";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-');
        }

        var name = builder.ToString().Trim('.');
        return name is "" or "." or ".." ? "run" : name;
    }

    /// <summary>Lowercase slug for branch names: <c>[a-z0-9-]</c>, collapsed, max <paramref name="maxLength"/>.</summary>
    public static string Slugify(string value, int maxLength = 48)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "task";
        }

        var builder = new StringBuilder(value.Length);
        var lastWasDash = true;
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0)
        {
            return "task";
        }

        return slug.Length > maxLength ? slug[..maxLength].TrimEnd('-') : slug;
    }

    /// <summary>
    /// Branch-name segment: ASCII letters/digits/<c>_-</c> only — dots excluded
    /// because git rejects <c>..</c> inside ref names.
    /// </summary>
    public static string SanitizeBranchPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "run";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '-');
        }

        var name = builder.ToString().Trim('-');
        return name.Length == 0 ? "run" : name;
    }

    /// <summary>Dedicated branch name <c>feature/agent-{runId}-{slug}</c>.</summary>
    public static string BranchName(string runId, string taskSlug)
        => $"feature/agent-{SanitizeBranchPart(runId)}-{Slugify(taskSlug)}";
}
