namespace Taskboard.Agents;

/// <summary>Agent CLIs managed from the CLI Agents page (SPEC-20260917-cli-agents-terminal).</summary>
public enum AgentCliKind
{
    Devin,
    Claude,
    Codex,
    OpenCode,
    Antigravity
}

/// <summary>Static description of an agent CLI: binary, credential probe and login/install commands.</summary>
public sealed record AgentCliSpec(
    string DisplayName,
    string Binary,
    string ConfigDirDisplay,
    string? CredentialRelativePath,
    string LoginCommand,
    string InstallHint);

/// <summary>
/// Maps each supported agent CLI to its binary name, config directory,
/// credential-file probe (existence only — contents are never read) and the
/// commands an admin runs to install/login (SPEC-20260917-cli-agents-terminal
/// RF-003).
/// </summary>
public static class AgentCliMap
{
    private static readonly IReadOnlyDictionary<AgentCliKind, AgentCliSpec> Specs =
        new Dictionary<AgentCliKind, AgentCliSpec>
        {
            [AgentCliKind.Claude] = new(
                "Claude Code",
                "claude",
                "~/.claude",
                ".claude/.credentials.json",
                "claude",
                "npm i -g @anthropic-ai/claude-code"),
            [AgentCliKind.Codex] = new(
                "Codex",
                "codex",
                "~/.codex",
                ".codex/auth.json",
                "codex login",
                "npm i -g @openai/codex"),
            [AgentCliKind.OpenCode] = new(
                "OpenCode",
                "opencode",
                "~/.config/opencode",
                ".local/share/opencode/auth.json",
                "opencode auth login",
                "npm i -g opencode-ai"),
            [AgentCliKind.Devin] = new(
                "Devin CLI",
                "devin",
                "~/.config/devin",
                ".local/share/devin/credentials.toml",
                "devin auth login",
                "curl -fsSL https://cli.devin.ai/install.sh | bash"),
            [AgentCliKind.Antigravity] = new(
                "Antigravity (agy)",
                "agy",
                "~/.gemini/antigravity-cli",
                ".gemini/antigravity-cli/antigravity-oauth-token",
                "agy",
                "curl -fsSL https://antigravity.google/cli/install.sh | bash"),
        };

    /// <summary>Returns the spec for <paramref name="kind"/>, or <c>null</c> for unknown members.</summary>
    public static AgentCliSpec? GetSpec(AgentCliKind kind) =>
        Specs.TryGetValue(kind, out var spec) ? spec : null;

    /// <summary>All known CLIs in display order.</summary>
    public static IReadOnlyList<KeyValuePair<AgentCliKind, AgentCliSpec>> All =>
        Specs.OrderBy(kv => kv.Value.DisplayName).ToList();
}
