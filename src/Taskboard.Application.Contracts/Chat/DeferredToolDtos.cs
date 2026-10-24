namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261023-p0-agent-hardening RF-D02: one catalog line for a tool whose
/// schema is withheld from the provider call — name + one-line description
/// only. <see cref="Available"/> reports the last availability probe.
/// </summary>
public sealed record DeferredToolEntry(string Name, string Description, bool Available);

/// <summary>
/// Per-turn partition of the effective tool set: <see cref="ActiveNames"/>
/// emit full JSON schemas (sorted), <see cref="Deferred"/> emit catalog lines.
/// Promoted tools are tracked by the catalog and appended separately.
/// </summary>
public sealed record ChatToolPlan(
    IReadOnlySet<string> ActiveNames,
    IReadOnlyList<DeferredToolEntry> Deferred);

/// <summary><c>tool_activate</c> outcome — <see cref="Activated"/> counts newly promoted tools (the <c>"*"</c> allow-all path).</summary>
public sealed record ToolActivateResult(bool Ok, string? Error, int Activated = 0);

/// <summary>
/// SPEC-20261023-p0-agent-hardening RF-D01/D02/D03: session-scoped deferred
/// tool catalog. Plans which tools emit schemas vs. catalog lines, TTL-caches
/// (60s) per-conversation availability probes, and records activations —
/// promotion is append-only so the provider cache prefix never shifts.
/// </summary>
public interface IDeferredToolCatalog
{
    /// <summary>
    /// Partitions <paramref name="toolSet"/> for a conversation: tools failing
    /// the availability probe or listed in <c>Taskboard:Chat:DeferredTools</c>
    /// are deferred; everything else is active. Meta-tools
    /// (<c>tool_search</c>/<c>tool_activate</c>) are always active.
    /// </summary>
    Task<ChatToolPlan> PlanAsync(
        string conversationId,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        ChatToolContext probeContext,
        CancellationToken cancellationToken);

    /// <summary>Deferred catalog entries for a conversation (empty before the first plan).</summary>
    IReadOnlyList<DeferredToolEntry> Deferred(string conversationId);

    /// <summary>Promoted tool names in activation order — schemas append to the wire tail.</summary>
    IReadOnlyList<string> PromotedNames(string conversationId);

    /// <summary>
    /// Promotes a deferred tool into the next provider call. <paramref name="name"/>
    /// of <c>"*"</c>/<c>"all"</c> activates every available deferred tool — the
    /// conversation-level allow-all debugging escape hatch (RF-D03).
    /// </summary>
    ToolActivateResult Activate(string conversationId, string name);

    /// <summary>Deferred entries matching <paramref name="query"/> on name or description; all entries when blank.</summary>
    IReadOnlyList<DeferredToolEntry> Search(string conversationId, string? query);
}
