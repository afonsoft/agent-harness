using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Single source of truth for each agent CLI's non-interactive invocation:
/// executable name plus the argument template that carries the prompt
/// (SPEC-20260918-agent-execution-ux RF-003).
/// </summary>
public static class AgentCliInvocation
{
    private static readonly Dictionary<AgentType, string> ExecutableNames = new()
    {
        [AgentType.Devin] = "devin",
        [AgentType.Claude] = "claude",
        [AgentType.Codex] = "codex",
        [AgentType.OpenCode] = "opencode",
        [AgentType.OpenHands] = "openhands",
        [AgentType.Antigravity] = "agy"
    };

    /// <summary>Executable name for <paramref name="agentType"/>, or null when unknown.</summary>
    public static string? ExecutableName(AgentType agentType) =>
        ExecutableNames.TryGetValue(agentType, out var name) ? name : null;

    /// <summary>
    /// Argument template per CLI. The prompt is always the last argument and is
    /// passed through the process argument list (never shell-interpolated).
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(AgentType agentType, string prompt) => agentType switch
    {
        // devin [PATH]... requires -p/--print for non-interactive mode; without it the prompt becomes a PATH.
        // --respect-workspace-trust false: print mode fails in an untrusted directory.
        AgentType.Devin => ["--respect-workspace-trust", "false", "-p", prompt],
        // claude -p for non-interactive mode; without a TTY permissions must be bypassed.
        AgentType.Claude => ["--dangerously-skip-permissions", "-p", prompt],
        // codex exec is the non-interactive mode; --approve-for-me auto-approves via workspace-write sandbox.
        AgentType.Codex => ["exec", "--approve-for-me", "--skip-git-repo-check", prompt],
        // opencode run executes a message and exits.
        AgentType.OpenCode => ["run", prompt],
        // agy -p/--print takes the prompt as the flag value and exits.
        AgentType.Antigravity => ["-p", prompt],
        _ => [prompt]
    };

    /// <summary>Full command line for preview, e.g. <c>devin -p &lt;prompt&gt;</c>.</summary>
    public static string PreviewCommandLine(AgentType agentType)
    {
        var executable = ExecutableName(agentType) ?? agentType.ToString().ToLowerInvariant();
        var arguments = BuildArguments(agentType, "<prompt>");
        return $"{executable} {string.Join(' ', arguments)}";
    }
}
