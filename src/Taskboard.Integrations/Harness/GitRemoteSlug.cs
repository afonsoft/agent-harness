namespace Taskboard.Integrations.Harness;

/// <summary>
/// Extracts the <c>owner/name</c> slug from a git remote URL pointing at
/// github.com. Supports <c>https://github.com/o/n(.git)</c>,
/// <c>git@github.com:o/n(.git)</c> and <c>ssh://git@github.com[:port]/o/n</c>.
/// Only github.com remotes resolve — the PR API targets api.github.com, so a
/// mirror (gitlab, GHES) must not masquerade as the target repository.
/// </summary>
public static class GitRemoteSlug
{
    public static string? TryParse(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return null;
        }

        var url = remoteUrl.Trim();
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^4];
        }
        url = url.TrimEnd('/');

        string host;
        string path;
        var schemeSep = url.IndexOf("://", StringComparison.Ordinal);
        var at = url.IndexOf('@');
        var colon = url.IndexOf(':');
        if (schemeSep >= 0)
        {
            // scheme://[user@]host[:port]/path
            var rest = url[(schemeSep + 3)..];
            var slash = rest.IndexOf('/');
            if (slash < 0)
            {
                return null;
            }
            host = rest[..slash];
            path = rest[(slash + 1)..];
            var atInHost = host.IndexOf('@');
            if (atInHost >= 0)
            {
                host = host[(atInHost + 1)..];
            }
            var port = host.IndexOf(':');
            if (port >= 0)
            {
                host = host[..port];
            }
        }
        else if (at >= 0 && colon > at)
        {
            // scp-like user@host:path
            host = url[(at + 1)..colon];
            path = url[(colon + 1)..];
        }
        else
        {
            var slash = url.IndexOf('/');
            if (slash < 0)
            {
                return null;
            }
            host = url[..slash];
            path = url[(slash + 1)..];
        }

        if (!host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !IsNamePart(parts[0]) || !IsNamePart(parts[1]))
        {
            return null;
        }

        return string.Concat(parts[0], "/", parts[1]);
    }

    private static bool IsNamePart(string segment) =>
        segment.Length > 0
        && segment.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
}
