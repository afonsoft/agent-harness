namespace Taskboard.Agents;

/// <summary>
/// Declarative registry mapping each managed <see cref="AgentCliKind"/> to the
/// SQLite databases it keeps on the host. Inventory verified on this host
/// 2026-09-19 (see <c>.specs/CAPABILITY-MAP-cli-metrics.md</c>); kinds without
/// a detected database register an empty list.
/// SPEC-20260919-cli-db-reader RF-001.
/// </summary>
public static class CliDatabaseMap
{
    private static readonly IReadOnlyList<CliDbSource> Empty = [];

    private static readonly IReadOnlyDictionary<AgentCliKind, IReadOnlyList<CliDbSource>> Sources =
        new Dictionary<AgentCliKind, IReadOnlyList<CliDbSource>>
        {
            [AgentCliKind.Codex] =
            [
                new CliDbSource(
                    "codex-state",
                    ".codex/*.sqlite",
                    ["sessions", "threads", "logs", "goals", "memories"],
                    ["credentials", "auth", "tokens"]),
            ],
            [AgentCliKind.OpenCode] =
            [
                new CliDbSource(
                    "opencode-db",
                    ".local/share/opencode/opencode.db",
                    ["session", "message", "part", "project"],
                    ["credential", "account", "account_state", "control_account", "permission"]),
            ],
            [AgentCliKind.Devin] =
            [
                new CliDbSource(
                    "devin-sessions",
                    ".local/share/devin/cli/sessions.db",
                    ["sessions"],
                    ["credentials", "auth", "tokens"]),
            ],
            [AgentCliKind.Antigravity] =
            [
                new CliDbSource(
                    "antigravity-conversations",
                    ".gemini/antigravity-cli/conversations/*.db",
                    ["conversation", "turn", "metadata"],
                    ["credentials", "auth", "tokens"]),
                new CliDbSource(
                    "antigravity-summaries",
                    ".gemini/antigravity-cli/conversation_summaries.db",
                    ["summaries"],
                    ["credentials", "auth", "tokens"]),
            ],
            [AgentCliKind.Cline] =
            [
                new CliDbSource(
                    "cline-data",
                    ".cline/data/db/*.db",
                    ["tasks", "messages"],
                    ["connectors", "credentials", "auth", "tokens"]),
            ],
            [AgentCliKind.Claude] =
            [
                new CliDbSource(
                    "claude-context-mode",
                    ".claude/context-mode/sessions/*.db",
                    ["sessions", "checkpoints"],
                    ["credentials", "auth", "tokens"],
                    Experimental: true),
            ],
            [AgentCliKind.Kimi] = Empty,
            [AgentCliKind.Grok] = Empty,
            [AgentCliKind.Aider] = Empty,
            [AgentCliKind.Continue] = Empty,
            [AgentCliKind.Copilot] = Empty,
            [AgentCliKind.Qwen] = Empty,
            [AgentCliKind.Kiro] = Empty,
        };

    /// <summary>Sources registered for <paramref name="kind"/> (empty when none detected).</summary>
    public static IReadOnlyList<CliDbSource> SourcesFor(AgentCliKind kind) =>
        Sources.TryGetValue(kind, out var list) ? list : Empty;

    /// <summary>All kinds that register at least one database source.</summary>
    public static IReadOnlyList<KeyValuePair<AgentCliKind, IReadOnlyList<CliDbSource>>> All =>
        Sources.Where(kv => kv.Value.Count > 0).OrderBy(kv => kv.Key.ToString()).ToList();
}
