using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261104-agent-attention-manifests RF-003: immutable input for the
/// detection engine — ANSI-stripped recent output plus the last OSC 0/1/2
/// title seen. Built from the session's tail buffer; the engine never sees
/// the raw PTY or the scrollable viewport.
/// </summary>
public sealed record DetectionSnapshot(
    /// <summary>Recent output, ANSI-stripped (bounded by the sink's tail).</summary>
    string Content,
    /// <summary>Latest OSC title captured, or null when none was emitted.</summary>
    string? OscTitle);

/// <summary>RF-007: one rule's outcome inside an evaluation (for explain).</summary>
public sealed record EvaluatedRuleDto(
    string Id,
    int Priority,
    string Region,
    bool Matched,
    /// <summary>First failing gate ("contains: \"x\"", "not gate", "region empty") when not matched.</summary>
    string? FailedGate);

/// <summary>
/// RF-002: engine verdict — pure result of evaluating one snapshot against
/// one manifest. <see cref="SuggestedState"/> is null when the verdict must
/// not move the state (matched a <c>skip_state_update</c>/unknown rule, or no
/// rule matched at all — <see cref="MatchedRuleId"/> distinguishes the two).
/// </summary>
public sealed record DetectionVerdict(
    AgentAttentionState? SuggestedState,
    string? MatchedRuleId,
    int? MatchedRulePriority,
    string? MatchedRegion,
    /// <summary>Manifest that produced the verdict — <c>bundled</c>|<c>override</c>|<c>none</c>.</summary>
    string ManifestSource,
    string? ManifestId,
    /// <summary>Evidence flags raised by the matched rule (visible_blocker etc).</summary>
    IReadOnlyList<string> Flags,
    IReadOnlyList<EvaluatedRuleDto> EvaluatedRules,
    /// <summary>Loader/evaluator notes — bad override fallback, incompatible manifest, unknown region.</summary>
    IReadOnlyList<string> Warnings);

/// <summary>RF-006: result of a detection reload (override rescan).</summary>
public sealed record DetectionReloadResult(
    int CliCount,
    int OverrideCount,
    IReadOnlyList<string> Warnings);

/// <summary>RF-007: last verdict recorded for a session (explain payload).</summary>
public sealed record AgentDetectionExplainDto(
    string SessionKey,
    AgentAttentionState? SuggestedState,
    string? ManifestId,
    string ManifestSource,
    EvaluatedRuleDto? MatchedRule,
    IReadOnlyList<EvaluatedRuleDto> EvaluatedRules,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> Warnings,
    string SnapshotHash,
    DateTime EvaluatedAtUtc);

/// <summary>
/// RF-002: pure manifest evaluation. Thread-safe — the implementation holds
/// an immutable manifest set swapped atomically on reload.
/// </summary>
public interface IAgentDetectionEngine
{
    /// <summary>
    /// Evaluates the snapshot against the manifest for <paramref name="cli"/>
    /// (resolved by id/alias, shell fallback otherwise). Rules run in priority
    /// order; the first full match wins; no match → null state + no rule.
    /// </summary>
    DetectionVerdict Evaluate(DetectionSnapshot snapshot, string? cli);

    /// <summary>Rescans override dirs and atomically swaps the manifest set.</summary>
    DetectionReloadResult Reload();
}

/// <summary>
/// RF-007: per-session ring of the last detection verdict — backs
/// <c>agent explain</c>. Recorded by the output-attention sink; dropped with
/// the session.
/// </summary>
public interface IAgentDetectionExplainStore
{
    void Record(string sessionKey, AgentDetectionExplainDto explain);
    AgentDetectionExplainDto? Get(string sessionKey);
    void Discard(string sessionKey);
}
