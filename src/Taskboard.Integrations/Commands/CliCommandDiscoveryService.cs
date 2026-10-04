using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Skills;

namespace Taskboard.Integrations.Commands;

/// <summary>
/// SPEC-20261004-cli-slash-commands RF-001/RF-002: discovers slash commands
/// and skills for one agent CLI by scanning its conventional directories
/// under the server user's home:
///
/// - command files: *.md (recursive); name = path relative to the commands
///   dir without extension ("ops/deploy.md" → "ops/deploy"); description =
///   frontmatter <c>description</c>, else the file's first non-blank line.
/// - skills: <c>*/SKILL.md</c> under the CLI's skills dir (frontmatter
///   name/description) — the per-CLI sources the chat catalog hides.
///
/// "command" wins name collisions over "skill". Unknown cli names and
/// missing dirs yield empty lists — discovery is best-effort.
/// </summary>
public sealed class CliCommandDiscoveryService : ICliCommandDiscoveryService
{
    private const int MaxEntries = 200;
    private const long MaxFileBytes = 256 * 1024;

    private sealed record CliLayout(string Source, string[] CommandDirs, string[] SkillDirs);

    private static readonly IReadOnlyDictionary<AgentCliKind, CliLayout> Layouts =
        new Dictionary<AgentCliKind, CliLayout>
        {
            [AgentCliKind.Claude] = new("claude",
                [".claude/commands"],
                [".claude/skills"]),
            [AgentCliKind.Codex] = new("codex",
                [".codex/prompts"],
                [".codex/skills"]),
            [AgentCliKind.OpenCode] = new("opencode",
                [".config/opencode/commands", ".opencode/commands"],
                [".config/opencode/skills", ".opencode/skills"]),
            [AgentCliKind.Devin] = new("devin",
                [".devin/commands", ".config/devin/commands"],
                [".devin/skills"]),
            [AgentCliKind.Antigravity] = new("agy",
                [".gemini/antigravity-cli/commands"],
                [".gemini/antigravity-cli/skills"]),
            [AgentCliKind.Kimi] = new("kimi",
                [".kimi-code/commands"],
                [".kimi-code/skills"]),
        };

    private readonly string _home;

    public CliCommandDiscoveryService(string homeDir)
    {
        _home = homeDir;
    }

    public Task<IReadOnlyList<CliCommandDto>> ListAsync(string cli, CancellationToken cancellationToken = default)
    {
        var layout = LayoutFor(cli);
        if (layout is null)
        {
            return Task.FromResult<IReadOnlyList<CliCommandDto>>([]);
        }

        var items = new List<CliCommandDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in layout.CommandDirs)
        {
            foreach (var (name, file) in EnumerateCommands(Path.Combine(_home, dir)))
            {
                if (items.Count >= MaxEntries || !seen.Add(name))
                {
                    continue;
                }

                items.Add(new CliCommandDto(name, Describe(file), "command", layout.Source));
            }
        }

        foreach (var dir in layout.SkillDirs)
        {
            foreach (var (name, file) in EnumerateSkills(Path.Combine(_home, dir)))
            {
                if (items.Count >= MaxEntries || !seen.Add(name))
                {
                    continue;
                }

                var fm = FrontmatterReader.Read(file);
                items.Add(new CliCommandDto(
                    fm?.Name ?? name,
                    fm?.Description ?? $"Skill {name}",
                    "skill",
                    layout.Source));
                if (fm is not null && !string.Equals(fm.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    seen.Add(fm.Name);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<CliCommandDto>>(
            items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly());
    }

    public Task<CliCommandDetailDto?> GetAsync(string cli, string name, CancellationToken cancellationToken = default)
    {
        var layout = LayoutFor(cli);
        if (layout is null || string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult<CliCommandDetailDto?>(null);
        }

        foreach (var dir in layout.CommandDirs)
        {
            foreach (var (cmdName, file) in EnumerateCommands(Path.Combine(_home, dir)))
            {
                if (!string.Equals(cmdName, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var (front, body) = SplitFrontmatter(File.ReadAllText(file));
                return Task.FromResult<CliCommandDetailDto?>(new CliCommandDetailDto(
                    cmdName,
                    front.Description.Length > 0 ? front.Description : FirstLine(body),
                    "command",
                    layout.Source,
                    body.Trim(),
                    front.ArgumentHint));
            }
        }

        foreach (var dir in layout.SkillDirs)
        {
            foreach (var (skillName, file) in EnumerateSkills(Path.Combine(_home, dir)))
            {
                var fm = FrontmatterReader.Read(file);
                var effectiveName = fm?.Name ?? skillName;
                if (!string.Equals(effectiveName, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return Task.FromResult<CliCommandDetailDto?>(new CliCommandDetailDto(
                    effectiveName,
                    fm?.Description ?? string.Empty,
                    "skill",
                    layout.Source,
                    File.ReadAllText(file),
                    null));
            }
        }

        return Task.FromResult<CliCommandDetailDto?>(null);
    }

    private static CliLayout? LayoutFor(string cli)
    {
        if (Enum.TryParse<AgentCliKind>(cli, ignoreCase: true, out var kind))
        {
            return Layouts.GetValueOrDefault(kind);
        }

        // Accept AgentType names too ("OpenCode", "Codex", …) so callers that
        // only know the thread's agent type resolve the same table.
        return Enum.TryParse<AgentType>(cli, ignoreCase: true, out var type)
            && AgentCliMap.CliKindFor(type) is { } mapped
            ? Layouts.GetValueOrDefault(mapped)
            : null;
    }

    private static IEnumerable<(string Name, string File)> EnumerateCommands(string dir)
    {
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
        {
            if (new FileInfo(file).Length > MaxFileBytes)
            {
                continue;
            }

            var relative = Path.GetRelativePath(dir, file);
            var name = Path.ChangeExtension(relative, null)?.Replace(Path.DirectorySeparatorChar, '/');
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return (name, file);
            }
        }
    }

    private static IEnumerable<(string Name, string File)> EnumerateSkills(string dir)
    {
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var skillDir in Directory.EnumerateDirectories(dir))
        {
            var skillFile = Path.Join(skillDir, "SKILL.md");
            if (File.Exists(skillFile))
            {
                yield return (Path.GetFileName(skillDir), skillFile);
            }
        }
    }

    private static string Describe(string file)
    {
        var (front, body) = SplitFrontmatter(File.ReadAllText(file));
        return front.Description.Length > 0 ? front.Description : FirstLine(body);
    }

    private static string FirstLine(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim().TrimStart('#').Trim();
            if (trimmed.Length > 0)
            {
                return trimmed.Length > 120 ? trimmed[..120] : trimmed;
            }
        }

        return string.Empty;
    }

    /// <summary>Splits a leading YAML-ish frontmatter block; command files use
    /// <c>description</c>/<c>argument-hint</c> (claude convention). Body keeps
    /// everything after the closing ---.</summary>
    private static (CommandFront Front, string Body) SplitFrontmatter(string content)
    {
        var lines = content.Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != "---")
        {
            return (new CommandFront(string.Empty, null), content);
        }

        var description = string.Empty;
        string? argumentHint = null;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Trim() == "---")
            {
                return (new CommandFront(description, argumentHint),
                    string.Join('\n', lines[(i + 1)..]));
            }

            if (TryField(line, "description", out var value))
            {
                description = value;
            }
            else if (TryField(line, "argument-hint", out value))
            {
                argumentHint = value;
            }
        }

        return (new CommandFront(string.Empty, null), content);
    }

    private static bool TryField(string line, string key, out string value)
    {
        value = string.Empty;
        var prefix = key + ":";
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        value = trimmed[prefix.Length..].Trim().Trim('"').Trim('\'');
        return true;
    }

    private sealed record CommandFront(string Description, string? ArgumentHint);
}
