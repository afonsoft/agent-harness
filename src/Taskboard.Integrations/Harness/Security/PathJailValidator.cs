using Taskboard.Harness;
using Taskboard.Workspace;

namespace Taskboard.Integrations.Harness.Security;

/// <summary>
/// Canonical path confinement (SPEC-20260919-harness-security-permission-gateway
/// RF-002). Resolves relative paths against the worktree, follows existing
/// symlinks and rejects anything landing outside the jail with
/// <see cref="SecurityAccessDeniedException"/>.
/// </summary>
public static class PathJailValidator
{
    /// <summary>
    /// Returns the canonical absolute path inside <paramref name="worktreePath"/>,
    /// or throws <see cref="SecurityAccessDeniedException"/>. Credential-paths
    /// matching <paramref name="denylist"/> deny regardless of tier or approval
    /// (SPEC-20261023-p0-agent-hardening RF-A01) — the exception carries only
    /// the pattern class (RF-A03), never the secret path.
    /// </summary>
    public static string Validate(
        string path, string worktreePath, SensitivePathDenylist? denylist = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new SecurityAccessDeniedException("Empty path.");
        }

        // ~, $VAR — expansões de shell nunca entram no jail.
        if (path.StartsWith('~') || path.StartsWith('$'))
        {
            throw new SecurityAccessDeniedException($"Path '{path}' escapes sandbox boundary.");
        }

        string full;
        try
        {
            full = Path.GetFullPath(path, worktreePath);
        }
        catch (Exception ex)
        {
            throw new SecurityAccessDeniedException($"Path '{path}' cannot be resolved: {ex.Message}");
        }

        if (!WorkspacePaths.IsUnder(worktreePath, full))
        {
            throw new SecurityAccessDeniedException($"Path '{path}' escapes sandbox boundary.");
        }

        var list = denylist ?? SensitivePathDenylist.Shared;
        if (list.TryMatch(full, out var cls))
        {
            throw new SecurityAccessDeniedException($"credential-path: {cls}");
        }

        // Symlink escape: se o caminho já existe, o alvo real também deve estar
        // dentro do jail (FileInfo.ResolveLinkTarget devolve o destino final).
        var resolved = ResolveLinks(full, worktreePath);
        if (!string.Equals(resolved, full, StringComparison.Ordinal)
            && list.TryMatch(resolved, out var resolvedCls))
        {
            throw new SecurityAccessDeniedException($"credential-path: {resolvedCls}");
        }

        return resolved;
    }

    private static string ResolveLinks(string full, string worktreePath)
    {
        // Sobe do caminho completo até a raiz resolvendo cada elo existente.
        var root = Path.GetFullPath(worktreePath).TrimEnd(Path.DirectorySeparatorChar);
        var current = full;

        while (current.Length > root.Length && current.StartsWith(root, StringComparison.Ordinal))
        {
            var resolved = ResolveIfLink(current);
            if (!string.Equals(resolved, current, StringComparison.Ordinal))
            {
                if (!WorkspacePaths.IsUnder(worktreePath, resolved))
                {
                    throw new SecurityAccessDeniedException(
                        $"Symlink '{full}' resolves outside the sandbox.");
                }

                return resolved;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current)
            {
                break;
            }

            current = parent;
        }

        return full;
    }

    private static string ResolveIfLink(string path)
    {
        try
        {
            FileSystemInfo? info;
            if (Directory.Exists(path))
            {
                info = new DirectoryInfo(path);
            }
            else
            {
                info = File.Exists(path) ? new FileInfo(path) : null;
            }

            if (info?.LinkTarget is { } target)
            {
                var final = info.ResolveLinkTarget(returnFinalTarget: true);
                var targetPath = final?.FullName
                    ?? (Path.IsPathRooted(target) ? target
                        : Path.GetFullPath(target, Path.GetDirectoryName(path)!));
                return Path.GetFullPath(targetPath);
            }
        }
        catch (IOException)
        {
            // Best-effort resolution — fall through on I/O errors.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort — failure here is non-fatal.
        }

        return path;
    }
}
