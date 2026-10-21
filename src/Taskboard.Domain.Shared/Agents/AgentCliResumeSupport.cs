namespace Taskboard.Agents;

/// <summary>
/// SPEC-20261020-agent-hibernation RF-004: hibernation resume support per CLI
/// — a conservative registry: a CLI hibernates only when killing it is safe
/// because a native resume command restores the session. Unknown → false.
/// P3: every known CLI is listed explicitly — <see cref="CliInfo.ResumeTemplate"/>
/// null means never hibernates; <see cref="CliInfo.EstimatedRssMb"/> feeds the
/// hibernation stats "saved memory" heuristic.
/// </summary>
public static class AgentCliResumeSupport
{
    /// <summary>Per-CLI hibernation info — resume argv template ("{id}"
    /// placeholder) or null, plus a rough resident-memory estimate.</summary>
    public sealed record CliInfo(IReadOnlyList<string>? ResumeTemplate, int EstimatedRssMb);

    /// <summary>cli slug → hibernation info. All scanner-known CLIs present.</summary>
    private static readonly IReadOnlyDictionary<string, CliInfo> Registry =
        new Dictionary<string, CliInfo>(StringComparer.Ordinal)
        {
            ["claude"] = new(["claude", "--resume", "{id}"], 180),
            ["codex"] = new(["codex", "resume", "{id}"], 150),
            ["opencode"] = new(["opencode", "--session", "{id}"], 120),
            ["gemini"] = new(["gemini", "--resume", "{id}"], 180),
            ["agy"] = new(null, 0),
            ["devin"] = new(null, 0),
        };

    /// <summary>True when the CLI can be safely hibernated (native resume).</summary>
    public static bool SupportsResume(string? cli) =>
        cli is not null && Registry.TryGetValue(cli, out var info)
            && info.ResumeTemplate is not null;

    /// <summary>
    /// argv for the resume spawn (binary + args with the session id filled),
    /// or null when the CLI has no native resume — devin/antigravity/unknown.
    /// </summary>
    public static IReadOnlyList<string>? ResumeArgv(string? cli, string sessionId) =>
        cli is not null && Registry.TryGetValue(cli, out var info)
            ? info.ResumeTemplate
                ?.Select(a => a.Replace("{id}", sessionId, StringComparison.Ordinal))
                .ToArray()
            : null;

    /// <summary>Rough resident-memory estimate per hibernated session (MB) —
    /// heuristic for the stats endpoint, not a measurement.</summary>
    public static int EstimatedRssMb(string? cli) =>
        cli is not null && Registry.TryGetValue(cli, out var info)
            ? info.EstimatedRssMb
            : 0;
}
