using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Curated model table per agent CLI (SPEC-20260918-agent-model-tiers RF-001).
/// Model names were checked against the CLIs installed on the host where
/// possible; entries marked "validate" rely on documented flags/names and
/// should be re-checked when the CLI is actually installed.
/// </summary>
public static class AgentCliModels
{
    private sealed record Entry(string Flag, string Lite, string Normal, string Ultra, string[] Catalog);

    private static readonly Dictionary<AgentType, Entry> Entries = new()
    {
        // claude --model accepts aliases (verified: 'haiku', 'sonnet', 'opus').
        [AgentType.Claude] = new("--model", "haiku", "sonnet", "opus",
            ["haiku", "sonnet", "opus", "claude-haiku-4-5", "claude-sonnet-4-5", "claude-opus-4-1"]),
        // codex exec -m <MODEL> — host-validated 2026-09-18: the ChatGPT
        // account rejects the whole gpt-5.1* family ("not supported when using
        // Codex with a ChatGPT account"); gpt-5.6-luna is the only working
        // name on this host and matches ~/.codex/config.toml. The gpt-5.1*
        // names stay in the catalog for API-key accounts — override via the
        // Models dialog.
        [AgentType.Codex] = new("-m", "gpt-5.6-luna", "gpt-5.6-luna", "gpt-5.6-luna",
            ["gpt-5.6-luna", "gpt-5.1-codex-mini", "gpt-5.1-codex", "gpt-5.1-codex-max", "gpt-5.1"]),
        // opencode -m takes provider/model (verified via `opencode models`).
        [AgentType.OpenCode] = new("-m", "opencode/claude-haiku-4-5", "opencode/claude-sonnet-5", "opencode/claude-opus-5",
            ["opencode/claude-haiku-4-5", "opencode/claude-sonnet-5", "opencode/claude-opus-5", "opencode/gpt-5.1", "opencode/gemini-3-pro"]),
        // devin --model accepts family names/aliases (verified via `devin models list`).
        [AgentType.Devin] = new("--model", "haiku", "swe", "opus",
            ["haiku", "swe", "opus", "swe-1.5", "swe-2"]),
        // agy --model (verified via `agy models` on the host).
        [AgentType.Antigravity] = new("--model", "gemini-3.8-flash-low", "gemini-3.1-pro-low", "gemini-3.1-pro-high",
            ["gemini-3.8-flash-low", "gemini-3.1-pro-low", "gemini-3.1-pro-high", "gemini-3.1-flash", "claude-sonnet-4-5"]),
        // Not installed on the host — flags/names from CLI docs, validate.
        [AgentType.Kimi] = new("--model", "kimi-k2", "kimi-k2", "kimi-k2-max",
            ["kimi-k2", "kimi-k2-max", "kimi-k2-instruct"]), // validate
        [AgentType.Grok] = new("--model", "grok-4-fast", "grok-4", "grok-4-heavy",
            ["grok-4-fast", "grok-4", "grok-4-heavy", "grok-3"]), // validate
        [AgentType.Aider] = new("--model", "deepseek", "sonnet", "opus",
            ["deepseek", "sonnet", "opus", "gpt-5.1", "gemini-3-pro"]), // validate
        [AgentType.Copilot] = new("--model", "claude-haiku-4-5", "claude-sonnet-4-5", "gpt-5.1",
            ["claude-haiku-4-5", "claude-sonnet-4-5", "gpt-5.1", "gpt-5.1-codex", "gemini-3-pro"]), // validate
        [AgentType.Qwen] = new("-m", "qwen3-coder-flash", "qwen3-coder-plus", "qwen3-max",
            ["qwen3-coder-flash", "qwen3-coder-plus", "qwen3-max", "qwen3-235b"]), // validate
        // Cline (-m takes provider-specific ids), Continue and Kiro have no
        // documented headless model flag → managed by the CLI itself.
    };

    // CLIs that can report their own model list headless (host-verified
    // 2026-09-18). Used to feed the Models dialog dropdown; CLIs without a
    // probe keep the curated catalog only.
    private static readonly Dictionary<AgentType, AgentModelListProbe> Probes = new()
    {
        // `opencode models` — one provider/model id per line.
        [AgentType.OpenCode] = new(["models"], AgentModelListFormat.Lines),
        // `agy models` — "id<TAB>Display Name" rows after a "Fetching…" banner.
        [AgentType.Antigravity] = new(["models"], AgentModelListFormat.TabSeparated),
        // `devin models list` — family headers + indented variant rows + aliases.
        [AgentType.Devin] = new(["models", "list"], AgentModelListFormat.DevinModelsList),
    };

    /// <summary>Whether the CLI accepts a headless model flag.</summary>
    public static bool SupportsModelSelection(AgentType agentType) =>
        Entries.ContainsKey(agentType);

    /// <summary>The argv flag used to pass the model, or null when CLI-managed.</summary>
    public static string? ModelFlag(AgentType agentType) =>
        Entries.TryGetValue(agentType, out var entry) ? entry.Flag : null;

    /// <summary>Known model names for the picker (curated + extras); empty when CLI-managed.</summary>
    public static IReadOnlyList<string> Catalog(AgentType agentType) =>
        Entries.TryGetValue(agentType, out var entry) ? entry.Catalog : [];

    /// <summary>Headless model-list probe for the CLI, or null when none is documented.</summary>
    public static AgentModelListProbe? ModelListProbe(AgentType agentType) =>
        Probes.TryGetValue(agentType, out var probe) ? probe : null;

    /// <summary>Resolved model name for the tier, or null when CLI-managed.</summary>
    public static string? ModelFor(AgentType agentType, AgentModelTier tier) =>
        Entries.TryGetValue(agentType, out var entry)
            ? tier switch
            {
                AgentModelTier.Lite => entry.Lite,
                AgentModelTier.Ultra => entry.Ultra,
                _ => entry.Normal,
            }
            : null;
}
