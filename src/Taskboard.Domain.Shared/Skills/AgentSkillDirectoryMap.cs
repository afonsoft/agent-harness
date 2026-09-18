using Taskboard.Agents;

namespace Taskboard.Skills;

/// <summary>
/// Maps each known agent CLI to the skills directories it reads under the user
/// profile (SPEC-20260915-skills-repo-sync RF-001).
/// </summary>
public static class AgentSkillDirectoryMap
{
    private static readonly IReadOnlyDictionary<AgentType, string[]> RelativePaths =
        new Dictionary<AgentType, string[]>
        {
            [AgentType.Devin] = [".devin/skills", ".config/devin/skills"],
            [AgentType.Claude] = [".claude/skills"],
            [AgentType.Codex] = [".codex/skills"],
            [AgentType.OpenCode] = [".opencode/skills", ".config/opencode/skills"],
            [AgentType.OpenHands] = [".openhands/skills"],
            [AgentType.Kimi] = [".kimi-code/skills"],
            [AgentType.Grok] = [".grok/skills"],
            // Aider has no agent-skills concept — mapped to empty so sync skips it.
            [AgentType.Aider] = [],
            [AgentType.Cline] = [".cline/skills"],
            [AgentType.Continue] = [".continue/skills"],
            [AgentType.Copilot] = [".copilot/skills"],
            [AgentType.Qwen] = [".qwen/skills"],
            [AgentType.Kiro] = [".kiro/skills"],
        };

    /// <summary>
    /// Returns the absolute skills directories for <paramref name="agentType"/>.
    /// Unknown members fall back to <c>~/.{enum-name-lowercase}/skills</c>.
    /// </summary>
    public static IReadOnlyList<string> GetSkillDirectories(AgentType agentType, string? homeDirectory = null)
    {
        var home = string.IsNullOrWhiteSpace(homeDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : homeDirectory;

        if (RelativePaths.TryGetValue(agentType, out var paths))
        {
            return paths.Select(p => Path.Join(home, p)).ToList().AsReadOnly();
        }

        var fallback = agentType.ToString().ToLowerInvariant();
        return new[] { Path.Join(home, $".{fallback}", "skills") };
    }
}
