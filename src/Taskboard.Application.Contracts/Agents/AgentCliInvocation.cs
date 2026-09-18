using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Single source of truth for each agent CLI's non-interactive invocation:
/// executable name plus the argument template that carries the prompt
/// (SPEC-20260918-agent-execution-ux RF-003) and the model flag
/// (SPEC-20260918-agent-model-tiers RF-002).
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
        [AgentType.Antigravity] = "agy",
        [AgentType.Kimi] = "kimi",
        [AgentType.Grok] = "grok",
        [AgentType.Aider] = "aider",
        [AgentType.Cline] = "cline",
        [AgentType.Continue] = "cn",
        [AgentType.Copilot] = "copilot",
        [AgentType.Qwen] = "qwen",
        [AgentType.Kiro] = "kiro-cli"
    };

    /// <summary>Executable name for <paramref name="agentType"/>, or null when unknown.</summary>
    public static string? ExecutableName(AgentType agentType) =>
        ExecutableNames.TryGetValue(agentType, out var name) ? name : null;

    /// <summary>
    /// Argument template per CLI. The prompt is always the last argument and is
    /// passed through the process argument list (never shell-interpolated).
    /// The model flag sits in the position each CLI expects — before the flag
    /// that introduces the prompt, never after it.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(
        AgentType agentType,
        string prompt,
        AgentModelTier tier = AgentModelTier.Normal,
        string? modelName = null)
    {
        var model = ModelArguments(agentType, tier, modelName);
        return agentType switch
        {
            // devin [PATH]... requires -p/--print for non-interactive mode; without it the prompt becomes a PATH.
            // --respect-workspace-trust false: print mode fails in an untrusted directory.
            AgentType.Devin => ["--respect-workspace-trust", "false", .. model, "-p", prompt],
            // claude -p for non-interactive mode; without a TTY permissions must be bypassed.
            AgentType.Claude => ["--dangerously-skip-permissions", .. model, "-p", prompt],
            // codex exec is the non-interactive mode; --approve-for-me auto-approves via workspace-write sandbox.
            AgentType.Codex => ["exec", "--approve-for-me", "--skip-git-repo-check", .. model, prompt],
            // opencode run executes a message and exits; --auto approves
            // permissions — headless mode cannot prompt and auto-denies.
            AgentType.OpenCode => ["run", "--auto", .. model, prompt],
            // agy -p/--print takes the prompt as the flag value and exits;
            // --dangerously-skip-permissions auto-approves tools — headless
            // mode cannot prompt and would auto-deny them otherwise.
            AgentType.Antigravity => ["--dangerously-skip-permissions", .. model, "-p", prompt],
            // kimi -p/--print runs the prompt headlessly and exits.
            AgentType.Kimi => [.. model, "-p", prompt],
            // grok -p runs headless; the CLI is non-interactive when a prompt is given.
            AgentType.Grok => [.. model, "-p", prompt],
            // aider --message runs one-shot; --yes-always confirms all prompts without a TTY.
            AgentType.Aider => ["--yes-always", .. model, "--message", prompt],
            // cline <prompt> starts in act mode with auto-approve enabled by default.
            AgentType.Cline => [prompt],
            // cn -p is headless print mode; --auto approves tool calls.
            AgentType.Continue => ["--auto", "-p", prompt],
            // copilot -p is programmatic mode; --allow-all-tools removes interactive approvals.
            AgentType.Copilot => ["--allow-all-tools", .. model, "-p", prompt],
            // qwen -p/--prompt runs headlessly (gemini-cli fork semantics).
            AgentType.Qwen => [.. model, "-p", prompt],
            // kiro-cli chat --no-interactive executes the prompt and exits; --trust-all-tools
            // pre-approves tool use so no TTY approval is needed.
            AgentType.Kiro => ["chat", "--no-interactive", "--trust-all-tools", prompt],
            _ => [prompt]
        };
    }

    /// <summary>Full command line for preview, e.g. <c>devin --model swe -p &lt;prompt&gt;</c>.</summary>
    public static string PreviewCommandLine(
        AgentType agentType,
        AgentModelTier tier = AgentModelTier.Normal)
    {
        var executable = ExecutableName(agentType) ?? agentType.ToString().ToLowerInvariant();
        var arguments = BuildArguments(agentType, "<prompt>", tier);
        return $"{executable} {string.Join(' ', arguments)}";
    }

    /// <summary>
    /// `[flag, model]` for CLIs with a curated mapping, empty for CLI-managed
    /// ones (Cline, Continue, Kiro, OpenHands) — they never get a model flag.
    /// </summary>
    private static string[] ModelArguments(AgentType agentType, AgentModelTier tier, string? modelName)
    {
        var flag = AgentCliModels.ModelFlag(agentType);
        // SPEC-20260918-agent-model-config RF-002: an explicitly resolved name
        // (orchestration → override ?? curated) wins over the static table.
        var model = string.IsNullOrWhiteSpace(modelName)
            ? AgentCliModels.ModelFor(agentType, tier)
            : modelName;
        return flag is not null && model is not null ? [flag, model] : [];
    }
}
