namespace Taskboard.Agents;

/// <summary>Agent CLIs managed from the CLI Agents page (SPEC-20260917-cli-agents-terminal).</summary>
public enum AgentCliKind
{
    Devin,
    Claude,
    Codex,
    OpenCode,
    Antigravity,
    Kimi,
    Grok,
    Aider,
    Cline,
    Continue,
    Copilot,
    Qwen,
    Kiro
}

/// <summary>
/// Static description of an agent CLI: binary, credential probe and login/install commands.
/// </summary>
public sealed record AgentCliSpec(
    string DisplayName,
    string Binary,
    string ConfigDirDisplay,
    string? CredentialRelativePath,
    string LoginCommand,
    string InstallHint,
    AgentCliInstallSpec Install);

/// <summary>
/// Executable installation command for a CLI. The command is a fixed,
/// server-side allowlisted entry — the client never supplies arguments
/// (SPEC-20260918-cli-agents-expansion RF-012). <see cref="RequiredTool"/>
/// names the prerequisite binary probed on PATH before the install runs
/// (e.g. <c>npm</c>, <c>pipx</c>, <c>curl</c>).
/// </summary>
public sealed record AgentCliInstallSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string RequiredTool);

/// <summary>
/// Maps each supported agent CLI to its binary name, config directory,
/// credential-file probe (existence only — contents are never read) and the
/// commands an admin runs to install/login (SPEC-20260917-cli-agents-terminal
/// RF-003, SPEC-20260918-cli-agents-expansion RF-001).
/// </summary>
public static class AgentCliMap
{
    private static AgentCliInstallSpec NpmInstall(string package) =>
        new("npm", ["install", "-g", package], "npm");

    private static AgentCliInstallSpec ScriptInstall(string url) =>
        new("bash", ["-c", $"curl -fsSL {url} | bash"], "curl");

    private static readonly IReadOnlyDictionary<AgentCliKind, AgentCliSpec> Specs =
        new Dictionary<AgentCliKind, AgentCliSpec>
        {
            [AgentCliKind.Claude] = new(
                "Claude Code",
                "claude",
                "~/.claude",
                ".claude/.credentials.json",
                "claude",
                "npm i -g @anthropic-ai/claude-code",
                NpmInstall("@anthropic-ai/claude-code")),
            [AgentCliKind.Codex] = new(
                "Codex",
                "codex",
                "~/.codex",
                ".codex/auth.json",
                "codex login",
                "npm i -g @openai/codex",
                NpmInstall("@openai/codex")),
            [AgentCliKind.OpenCode] = new(
                "OpenCode",
                "opencode",
                "~/.config/opencode",
                ".local/share/opencode/auth.json",
                "opencode auth login",
                "npm i -g opencode-ai",
                NpmInstall("opencode-ai")),
            [AgentCliKind.Devin] = new(
                "Devin CLI",
                "devin",
                "~/.config/devin",
                ".local/share/devin/credentials.toml",
                "devin auth login",
                "curl -fsSL https://cli.devin.ai/install.sh | bash",
                ScriptInstall("https://cli.devin.ai/install.sh")),
            [AgentCliKind.Antigravity] = new(
                "Antigravity (agy)",
                "agy",
                "~/.gemini/antigravity-cli",
                ".gemini/antigravity-cli/antigravity-oauth-token",
                "agy",
                "curl -fsSL https://antigravity.google/cli/install.sh | bash",
                ScriptInstall("https://antigravity.google/cli/install.sh")),
            [AgentCliKind.Kimi] = new(
                "Kimi Code",
                "kimi",
                "~/.kimi-code",
                null,
                "kimi",
                "curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash",
                ScriptInstall("https://code.kimi.com/kimi-code/install.sh")),
            [AgentCliKind.Grok] = new(
                "Grok (x.ai)",
                "grok",
                "~/.grok",
                null,
                "grok",
                "curl -fsSL https://x.ai/cli/install.sh | bash",
                ScriptInstall("https://x.ai/cli/install.sh")),
            [AgentCliKind.Aider] = new(
                "Aider",
                "aider",
                "~/.aider",
                null,
                "aider",
                "pipx install aider-chat",
                new AgentCliInstallSpec("pipx", ["install", "aider-chat"], "pipx")),
            [AgentCliKind.Cline] = new(
                "Cline",
                "cline",
                "~/.cline",
                ".cline/data/settings/providers.json",
                "cline auth",
                "npm i -g cline",
                NpmInstall("cline")),
            [AgentCliKind.Continue] = new(
                "Continue",
                "cn",
                "~/.continue",
                null,
                "cn",
                "npm i -g @continuedev/cli",
                NpmInstall("@continuedev/cli")),
            [AgentCliKind.Copilot] = new(
                "GitHub Copilot CLI",
                "copilot",
                "~/.copilot",
                null,
                "copilot",
                "npm i -g @github/copilot",
                NpmInstall("@github/copilot")),
            [AgentCliKind.Qwen] = new(
                "Qwen Code",
                "qwen",
                "~/.qwen",
                ".qwen/oauth_creds.json",
                "qwen",
                "npm i -g @qwen-code/qwen-code@latest",
                NpmInstall("@qwen-code/qwen-code@latest")),
            [AgentCliKind.Kiro] = new(
                "Kiro CLI",
                "kiro-cli",
                "~/.kiro",
                null,
                "kiro-cli login",
                "curl -fsSL https://cli.kiro.dev/install | bash",
                ScriptInstall("https://cli.kiro.dev/install")),
        };

    /// <summary>Returns the spec for <paramref name="kind"/>, or <c>null</c> for unknown members.</summary>
    public static AgentCliSpec? GetSpec(AgentCliKind kind) =>
        Specs.TryGetValue(kind, out var spec) ? spec : null;

    /// <summary>All known CLIs in display order.</summary>
    public static IReadOnlyList<KeyValuePair<AgentCliKind, AgentCliSpec>> All =>
        Specs.OrderBy(kv => kv.Value.DisplayName).ToList();

    private static readonly IReadOnlyDictionary<AgentCliKind, AgentType> CliToType =
        new Dictionary<AgentCliKind, AgentType>
        {
            [AgentCliKind.Devin] = AgentType.Devin,
            [AgentCliKind.Claude] = AgentType.Claude,
            [AgentCliKind.Codex] = AgentType.Codex,
            [AgentCliKind.OpenCode] = AgentType.OpenCode,
            [AgentCliKind.Antigravity] = AgentType.Antigravity,
            [AgentCliKind.Kimi] = AgentType.Kimi,
            [AgentCliKind.Grok] = AgentType.Grok,
            [AgentCliKind.Aider] = AgentType.Aider,
            [AgentCliKind.Cline] = AgentType.Cline,
            [AgentCliKind.Continue] = AgentType.Continue,
            [AgentCliKind.Copilot] = AgentType.Copilot,
            [AgentCliKind.Qwen] = AgentType.Qwen,
            [AgentCliKind.Kiro] = AgentType.Kiro,
        };

    /// <summary>Returns the orchestration <see cref="AgentType"/> for a CLI kind, or <c>null</c> for unknown kinds.</summary>
    public static AgentType? AgentTypeFor(AgentCliKind kind) =>
        CliToType.TryGetValue(kind, out var type) ? type : null;

    /// <summary>
    /// Returns the CLI kind backing an <see cref="AgentType"/>, or <c>null</c> when the type
    /// has no detectable CLI (e.g. <see cref="AgentType.OpenHands"/>).
    /// </summary>
    public static AgentCliKind? CliKindFor(AgentType type)
    {
        foreach (var kv in CliToType)
        {
            if (kv.Value == type)
            {
                return kv.Key;
            }
        }

        return null;
    }
}
