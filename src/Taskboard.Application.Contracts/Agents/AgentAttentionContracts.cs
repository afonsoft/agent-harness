using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261020-agent-attention-model RF-002: a single attention signal
/// published into the evaluator. <paramref name="SessionKey"/> is
/// "<c>kind:id</c>"; <paramref name="SuggestedState"/> is what the producer
/// believes — the evaluator applies hysteresis before committing.
/// </summary>
public sealed record AttentionSignal(
    string SessionKey,
    string Cli,
    AttentionSignalKind Kind,
    AgentAttentionState SuggestedState,
    string? Reason = null,
    string? Evidence = null,
    DateTime? AtUtc = null);

/// <summary>Snapshot row served to the board/feed (RF-008).</summary>
public sealed record AgentSessionStateDto(
    string SessionKey,
    string Cli,
    AgentAttentionState State,
    string? AttentionReason,
    DateTime StateChangedAtUtc,
    DateTime LastSignalAtUtc,
    AttentionSignalKind LastSignalKind,
    string? Evidence,
    long Version,
    int WeakSignalCount);

/// <summary>Transition ledger row (RF-008 audit).</summary>
public sealed record AgentSessionStateTransitionDto(
    string SessionKey,
    AgentAttentionState FromState,
    AgentAttentionState ToState,
    AttentionSignalKind SignalKind,
    string? Reason,
    DateTime AtUtc);

/// <summary>Persistence boundary — entity stays inside the EF layer.</summary>
public interface IAgentSessionStateRepository
{
    Task<AgentSessionStateDto?> GetAsync(string sessionKey, CancellationToken ct = default);
    Task<IReadOnlyList<AgentSessionStateDto>> ListAsync(
        IReadOnlyCollection<string>? keys = null, int take = 200, CancellationToken ct = default);

    /// <summary>
    /// Applies an evaluated transition inside the store. Returns the updated
    /// dto and whether the state actually changed (a transition row is only
    /// appended when it did).
    /// </summary>
    Task<(AgentSessionStateDto State, bool Changed)> ApplyAsync(
        AttentionSignal signal, AgentAttentionState next, int weakSignalCount,
        bool resetWeak, DateTime atUtc, CancellationToken ct = default);

    Task EnsureAsync(string sessionKey, string cli, DateTime atUtc, CancellationToken ct = default);

    /// <summary>Sessions whose last signal is older than <paramref name="beforeUtc"/>.</summary>
    Task<IReadOnlyList<AgentSessionStateDto>> ListSilentAsync(
        DateTime beforeUtc, int take = 100, CancellationToken ct = default);

    Task<IReadOnlyList<AgentSessionStateTransitionDto>> ListTransitionsAsync(
        string sessionKey, int take = 100, CancellationToken ct = default);
}

/// <summary>
/// SPEC-20261020-agent-attention-model RF-005/RF-006: consumes raw PTY/stdout
/// chunks, keeps a ≤4KB tail ring per session, extracts OSC titles and
/// pattern-matches the tail — publishing weak signals into the evaluator
/// (debounced ≥2s per session).
/// </summary>
public interface IAgentOutputAttentionSink
{
    /// <param name="cli">Binary/display name; resolved via AgentCliAttentionPatterns.</param>
    void ObserveOutput(string sessionKey, string? cli, string chunk);

    /// <summary>Drops the session tail buffer when the session closes.</summary>
    void Discard(string sessionKey);
}

/// <summary>
/// SPEC-20261020-agent-attention-model RF-003: per-session hook tokens —
/// issued when a session starts, injected as HARNESS_AGENT_HOOK_TOKEN,
/// rotated per session, never logged.
/// </summary>
public interface IAgentHookTokenService
{
    /// <summary>Issues (or rotates) the token for a session.</summary>
    string Issue(string sessionKey);

    /// <summary>Maps a presented token back to its session — null when unknown.</summary>
    string? Validate(string token);

    /// <summary>Best-effort cleanup when a session ends.</summary>
    void Revoke(string sessionKey);
}

/// <summary>POST /api/agent-hooks body from a managed hook.</summary>
public sealed record AgentHookEvent(
    string SessionKey,
    /// <summary>permission | question | turn_start | turn_end | stop | session_end | notification</summary>
    string Event,
    string? Payload);

/// <summary>
/// RF-002/RF-008: single entry point for producers → evaluator → store →
/// broadcast. Evaluator is serialized per session key.
/// </summary>
public interface IAgentAttentionService
{
    Task<AgentSessionStateDto> PublishAsync(AttentionSignal signal, CancellationToken ct = default);
    Task<IReadOnlyList<AgentSessionStateDto>> SnapshotAsync(
        IReadOnlyCollection<string>? keys = null, CancellationToken ct = default);

    /// <summary>Marks sessions silent for longer than the window as unverifiable.</summary>
    Task<int> SweepUnverifiableAsync(TimeSpan window, CancellationToken ct = default);
}

/// <summary>Broadcast of a committed state transition — board/badge/feed subscribe.</summary>
public interface IAgentAttentionBroadcaster
{
    Task BroadcastAsync(AgentSessionStateDto state, CancellationToken ct = default);
}
