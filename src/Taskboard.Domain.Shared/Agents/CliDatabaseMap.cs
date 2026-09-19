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

    private static readonly IReadOnlyList<string> GenericDenied =
        ["credential", "credentials", "account", "account_state", "control_account", "permission", "auth", "tokens"];

    private static readonly IReadOnlyDictionary<AgentCliKind, IReadOnlyList<CliDbSource>> Sources =
        new Dictionary<AgentCliKind, IReadOnlyList<CliDbSource>>
        {
            // Codex shards its state; `state_*.sqlite` holds the `threads` session table.
            [AgentCliKind.Codex] =
            [
                new CliDbSource(
                    "codex-state",
                    ".codex/state_*.sqlite",
                    ["threads"],
                    GenericDenied),
            ],
            [AgentCliKind.OpenCode] =
            [
                new CliDbSource(
                    "opencode-db",
                    ".local/share/opencode/opencode.db",
                    ["session"],
                    ["credential", "account", "account_state", "control_account", "permission"]),
            ],
            [AgentCliKind.Devin] =
            [
                new CliDbSource(
                    "devin-sessions",
                    ".local/share/devin/cli/sessions.db",
                    ["sessions"],
                    GenericDenied),
            ],
            [AgentCliKind.Antigravity] =
            [
                new CliDbSource(
                    "antigravity-summaries",
                    ".gemini/antigravity-cli/conversation_summaries.db",
                    ["conversation_summaries"],
                    GenericDenied),
                // Per-conversation DBs are registered for status reporting; the v1
                // extractor only reads the summaries database.
                new CliDbSource(
                    "antigravity-conversations",
                    ".gemini/antigravity-cli/conversations/*.db",
                    ["steps", "gen_metadata", "trajectory_meta"],
                    GenericDenied),
            ],
            // `connectors.db` holds credentials — the pattern only matches event DBs.
            [AgentCliKind.Cline] =
            [
                new CliDbSource(
                    "cline-hub-events",
                    ".cline/data/db/hub-events-*.db",
                    ["hub_events"],
                    ["connectors", .. GenericDenied]),
            ],
            [AgentCliKind.Claude] =
            [
                new CliDbSource(
                    "claude-context-mode",
                    ".claude/context-mode/sessions/*.db",
                    ["session_meta"],
                    GenericDenied,
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
