using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261006 RF-002: scans each CLI's on-disk session transcripts and
/// builds the native resume command. Read-only: file names and mtimes only,
/// never transcript contents. Missing directories → empty, never errors.
/// </summary>
public sealed class AgentSessionScanner : IAgentSessionScanner
{
    private readonly string _home;

    public AgentSessionScanner()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    internal AgentSessionScanner(string home)
    {
        _home = home;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentSessionInfoDto>> ScanAsync(
        string? cli = null, int takePerCli = 50, CancellationToken ct = default)
    {
        var sessions = new List<AgentSessionInfoDto>();

        if (Include(cli, "claude"))
        {
            sessions.AddRange(ScanClaude(takePerCli));
        }

        if (Include(cli, "codex"))
        {
            sessions.AddRange(ScanCodex(takePerCli));
        }

        if (Include(cli, "opencode"))
        {
            sessions.AddRange(ScanOpenCode(takePerCli));
        }

        return Task.FromResult<IReadOnlyList<AgentSessionInfoDto>>(
            sessions.OrderByDescending(s => s.ModifiedAtUtc).ToList());
    }

    private static bool Include(string? cli, string name) =>
        string.IsNullOrWhiteSpace(cli)
        || string.Equals(cli, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>~/.claude/projects/&lt;slug&gt;/&lt;id&gt;.jsonl — slug is the cwd with separators flattened.</summary>
    private IEnumerable<AgentSessionInfoDto> ScanClaude(int take)
    {
        var spec = AgentCliMap.GetSpec(AgentCliKind.Claude);
        var root = Path.Join(_home, ".claude", "projects");
        foreach (var file in NewestFiles(root, "*.jsonl", take))
        {
            var slug = Path.GetFileName(Path.GetDirectoryName(file)!) ?? string.Empty;
            var id = Path.GetFileNameWithoutExtension(file);
            yield return new AgentSessionInfoDto(
                "claude",
                id,
                DecodeSlug(slug),
                File.GetLastWriteTimeUtc(file),
                file,
                ResumeCommand(spec, id));
        }
    }

    /// <summary>~/.codex/sessions/**/rollout-*.jsonl — session id is the trailing uuid.</summary>
    private IEnumerable<AgentSessionInfoDto> ScanCodex(int take)
    {
        var spec = AgentCliMap.GetSpec(AgentCliKind.Codex);
        var root = Path.Join(_home, ".codex", "sessions");
        foreach (var file in NewestFiles(root, "rollout-*.jsonl", take))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var sessionId = TrailingUuid(stem) ?? stem;
            yield return new AgentSessionInfoDto(
                "codex",
                sessionId,
                null,
                File.GetLastWriteTimeUtc(file),
                file,
                ResumeCommand(spec, sessionId));
        }
    }

    /// <summary>~/.local/share/opencode/**/storage/session/*.json — session ids like ses_*.</summary>
    private IEnumerable<AgentSessionInfoDto> ScanOpenCode(int take)
    {
        var spec = AgentCliMap.GetSpec(AgentCliKind.OpenCode);
        var root = Path.Join(_home, ".local", "share", "opencode");
        var files = NewestFiles(root, "*.json", take * 4)
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}session{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.GetFileName(f).StartsWith("ses_", StringComparison.Ordinal))
            .Take(take);
        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            yield return new AgentSessionInfoDto(
                "opencode",
                id,
                null,
                File.GetLastWriteTimeUtc(file),
                file,
                ResumeCommand(spec, id));
        }
    }

    /// <summary>Files of <paramref name="root"/> matching <paramref name="pattern"/>,
    /// newest-first, capped. Missing/unreadable trees → empty, never throws.</summary>
    private static IReadOnlyList<string> NewestFiles(string root, string pattern, int take)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(take)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Binary + native resume argv; null when the spec/cli has no resume.</summary>
    private static string? ResumeCommand(AgentCliSpec? spec, string sessionId) =>
        spec?.BuildResumeArgs(sessionId) is { } args
            ? $"{spec.Binary} {string.Join(' ', args)}"
            : null;

    /// <summary>Claude flattens the cwd into the project dir name (<c>-</c> for <c>/</c>).</summary>
    private static string? DecodeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var decoded = slug.Replace('-', '/');
        return decoded.StartsWith('/') ? decoded : $"/{decoded}";
    }

    /// <summary>Rollout file names end with the session uuid.</summary>
    private static string? TrailingUuid(string stem)
    {
        var tail = stem.Length > 36 ? stem[^36..] : stem;
        return Guid.TryParse(tail, out _) ? tail : null;
    }
}
