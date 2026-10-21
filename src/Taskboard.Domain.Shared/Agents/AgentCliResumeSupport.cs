namespace Taskboard.Agents;

/// <summary>
/// SPEC-20261020-agent-hibernation RF-004: hibernation resume support per CLI
/// — a conservative registry: a CLI hibernates only when killing it is safe
/// because a native resume command restores the session. Unknown → false.
/// </summary>
public static class AgentCliResumeSupport
{
    /// <summary>cli slug → (binary + resume argv template, "{id}" placeholder).</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Templates =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["claude"] = ["claude", "--resume", "{id}"],
            ["codex"] = ["codex", "resume", "{id}"],
            ["opencode"] = ["opencode", "--session", "{id}"],
            ["gemini"] = ["gemini", "--resume", "{id}"],
        };

    /// <summary>True when the CLI can be safely hibernated (native resume).</summary>
    public static bool SupportsResume(string? cli) =>
        cli is not null && Templates.ContainsKey(cli);

    /// <summary>
    /// argv for the resume spawn (binary + args with the session id filled),
    /// or null when the CLI has no native resume — devin/antigravity/unknown.
    /// </summary>
    public static IReadOnlyList<string>? ResumeArgv(string? cli, string sessionId) =>
        cli is not null && Templates.TryGetValue(cli, out var tpl)
            ? tpl.Select(a => a.Replace("{id}", sessionId, StringComparison.Ordinal)).ToArray()
            : null;
}
