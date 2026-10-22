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
        string? modelName = null,
        bool omitModelFlag = false,
        IReadOnlyList<string>? extraArgs = null,
        AgentAutonomy autonomy = AgentAutonomy.Edit)
    {
        var model = omitModelFlag ? [] : ModelArguments(agentType, tier, modelName);
        // SPEC-20261021-agent-automations RF-005: whitelisted pass-through
        // flags splice BEFORE the prompt — trailing args are swallowed as
        // prompt text on exec-style CLIs (codex/kimi/qwen).
        IReadOnlyList<string> extra = extraArgs ?? [];
        // SPEC-20261022-cli-autonomy-profiles RF-002: the leading segment is
        // the autonomy profile for the level — Edit reproduces the historical
        // always-max flags byte-for-byte.
        var auto = AgentCliAutonomy.Flags(agentType, autonomy);
        return agentType switch
        {
            // devin [PATH]... requires -p/--print for non-interactive mode; without it the prompt becomes a PATH.
            AgentType.Devin => [.. auto, .. model, .. extra, "-p", prompt],
            // claude -p for non-interactive mode.
            AgentType.Claude => [.. auto, .. model, .. extra, "-p", prompt],
            // codex exec <prompt> — prompt is positional.
            AgentType.Codex => [.. auto, .. model, .. extra, prompt],
            // opencode run <prompt> — prompt is positional.
            AgentType.OpenCode => [.. auto, .. model, .. extra, prompt],
            // agy -p/--print takes the prompt as the flag value and exits.
            AgentType.Antigravity => [.. auto, .. model, .. extra, "-p", prompt],
            // kimi -p/--print runs the prompt headlessly and exits.
            AgentType.Kimi => [.. model, .. extra, "-p", prompt],
            // grok -p runs headless; the CLI is non-interactive when a prompt is given.
            AgentType.Grok => [.. model, .. extra, "-p", prompt],
            // aider --message runs one-shot.
            AgentType.Aider => [.. auto, .. model, .. extra, "--message", prompt],
            // cline <prompt> starts in act mode.
            AgentType.Cline => [.. extra, prompt],
            // cn -p is headless print mode.
            AgentType.Continue => [.. auto, .. extra, "-p", prompt],
            // copilot -p is programmatic mode.
            AgentType.Copilot => [.. auto, .. model, .. extra, "-p", prompt],
            // qwen -p/--prompt runs headlessly (gemini-cli fork semantics).
            AgentType.Qwen => [.. model, .. extra, "-p", prompt],
            // kiro-cli chat <prompt> — prompt is positional.
            AgentType.Kiro => [.. auto, .. extra, prompt],
            _ => [.. extra, prompt]
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
