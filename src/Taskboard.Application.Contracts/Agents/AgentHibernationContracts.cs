namespace Taskboard.Application.Contracts.Agents;

/// <summary>SPEC-20261020-agent-hibernation RF-001: wire shape of a record.</summary>
public sealed record AgentHibernationDto(
    string SessionKey,
    string UserId,
    string Cli,
    bool IsHibernated,
    DateTime HibernatedAtUtc,
    DateTime IdleAtUtc,
    IReadOnlyList<string> ResumeArgv,
    /// <summary>The CLI's own session id — exact join onto index rows.</summary>
    string? NativeSessionId,
    string? Cwd,
    int AutoResumedCount,
    bool Pinned,
    /// <summary>SPEC-20261022-mailbox-wake RF-002: storm-throttle stamp.</summary>
    DateTime? LastAutoResumeUtc = null);

/// <summary>Persistence boundary — the entity stays inside the EF layer.</summary>
public interface IAgentHibernationRepository
{
    Task<AgentHibernationDto?> GetAsync(string sessionKey, CancellationToken ct = default);
    Task<IReadOnlyList<AgentHibernationDto>> ListAsync(
        string? userId = null, CancellationToken ct = default);
    /// <summary>Session keys currently hibernated — cheap join for cards/index.</summary>
    Task<HashSet<string>> ListKeysAsync(CancellationToken ct = default);
    Task AddAsync(string sessionKey, string userId, string cli, string resumeArgvJson,
        string? nativeSessionId, string? cwd, DateTime idleAtUtc, DateTime atUtc,
        CancellationToken ct = default);
    /// <summary>Creates the pin marker row for a live session (RF-009).</summary>
    Task PinOnlyAsync(string sessionKey, string userId, string cli,
        string? nativeSessionId, string? cwd, DateTime atUtc,
        CancellationToken ct = default);
    Task RemoveAsync(string sessionKey, CancellationToken ct = default);
    Task SetPinnedAsync(string sessionKey, bool pinned, CancellationToken ct = default);
    /// <summary>Clears the hibernated flag; bumps AutoResumedCount on auto resume.</summary>
    Task MarkResumedAsync(string sessionKey, bool auto, CancellationToken ct = default);

    /// <summary>SPEC-20261022-mailbox-wake RF-002: stamps an auto-resume
    /// attempt — called before respawning so failures still throttle.</summary>
    Task MarkAutoResumeAttemptAsync(string sessionKey, DateTime atUtc, CancellationToken ct = default);
}

/// <summary>Context handed to each hibernation guard (RF-003).</summary>
public sealed record HibernationContext(
    AgentSessionStateDto State,
    AgentHibernationDto? Record,
    DateTime NowUtc,
    TimeSpan IdleThreshold);

/// <summary>
/// RF-003: one guard in the hibernation chain — returns a refusal reason or
/// null when the session may hibernate. Reasons surface on the card tooltip.
/// </summary>
public interface IAgentHibernationGuard
{
    string Name { get; }
    Task<string?> CheckAsync(HibernationContext ctx, CancellationToken ct = default);
}

/// <summary>Result of a hibernate attempt.</summary>
public sealed record HibernateOutcome(bool Hibernated, string? Reason = null);

/// <summary>Result of a resume attempt.</summary>
public sealed record ResumeOutcome(bool Resumed, string? SessionKey = null, string? Reason = null);

/// <summary>RF-006: outcome of a focus event on a hibernated row.</summary>
public sealed record FocusOutcome(
    bool Hibernated, bool Resumed, bool Resumable, string? Reason = null);

/// <summary>RF-006/RF-010 + mailbox-wake RF-002: per-user hibernation preferences.</summary>
public sealed record HibernationPrefsDto(
    int IdleThresholdMinutes, bool AutoResumeOnFocus, bool AutoResumeOnMailbox = true);

/// <summary>P3: hibernation stats — counts + heuristic memory saved.</summary>
public sealed record HibernationStatsDto(
    int HibernatedCount,
    int PinnedCount,
    int AutoResumedTotal,
    int EstimatedRssSavedMb);

/// <summary>RF-007: env markers injected into a respawned session's process.</summary>
public static class HibernationEnv
{
    public const string Resumed = "HARNESS_RESUMED";
    public const string ResumeOf = "HARNESS_RESUME_OF";
}

/// <summary>
/// Kills the live runtime session behind a session key — term:{id} → PTY,
/// issue:{threadId} → ACP. Implemented in Server over the session managers.
/// </summary>
public interface ISessionTerminator
{
    /// <summary>Whether the key maps to a running runtime session.</summary>
    bool IsAlive(string sessionKey);

    /// <summary>Graceful kill + PTY release; false when nothing was live.</summary>
    Task<bool> TerminateAsync(string sessionKey, CancellationToken ct = default);
}

/// <summary>
/// RF-007: respawns a hibernated session — PTY in the original cwd running
/// <paramref name="argv"/> with the env markers (<c>HARNESS_RESUMED=1</c>,
/// <c>HARNESS_RESUME_OF=&lt;key&gt;</c>), detached until the user reattaches.
/// Idempotent: a live session under the key is reused, never duplicated.
/// </summary>
public interface ISessionRespawner
{
    Task<bool> RespawnAsync(
        string sessionKey, string userKey, IReadOnlyList<string> argv,
        string? cwd, CancellationToken ct = default);
}

/// <summary>
/// SPEC-20261022-mailbox-wake: resolves an addressed mailbox recipient to
/// hibernated session rows and auto-resumes them (preference-gated,
/// ≥60s per-session throttle). Broadcast-only addresses never wake.
/// Singleton — resolves its own scope so send paths can fire-and-forget.
/// </summary>
public interface IMailboxWakeService
{
    /// <summary>Fire when a mailbox message lands for <paramref name="recipient"/>
    /// (<c>&lt;cli&gt;</c>, <c>&lt;taskId&gt;</c> or <c>@worktree:&lt;runId&gt;</c>);
    /// <c>@all</c>/<c>@idle</c> and unresolvable recipients are skipped silently.</summary>
    Task WakeForRecipientAsync(string? recipient, CancellationToken ct = default);
}

/// <summary>
/// SPEC-20261020-agent-hibernation: hibernate/resume pipeline — the 60s job
/// scans candidates, manual actions use the same path minus the threshold.
/// </summary>
public interface IAgentHibernationService
{
    /// <summary>One scan tick — hibernates done/idle sessions past the
    /// threshold, ≤50 candidates per pass.</summary>
    Task<int> ScanOnceAsync(string userId, TimeSpan idleThreshold, CancellationToken ct = default);

    /// <summary>RF-005: manual hibernate — guards apply, threshold skipped.</summary>
    Task<HibernateOutcome> HibernateAsync(
        string userId, string sessionKey, CancellationToken ct = default);

    /// <summary>RF-005/RF-007: respawn a hibernated session. <paramref name="auto"/>
    /// bumps AutoResumedCount (focus-triggered auto-resume, RF-006).
    /// <paramref name="reason"/> lands on the SessionResumed feed body
    /// (e.g. <c>"mailbox"</c> — SPEC-20261022-mailbox-wake).</summary>
    Task<ResumeOutcome> ResumeAsync(
        string userId, string sessionKey, bool auto = false,
        string? reason = null, CancellationToken ct = default);

    /// <summary>RF-006: focus event — auto-resumes when the user pref allows,
    /// else reports the session as resumable for the UI Resume button.</summary>
    Task<FocusOutcome> FocusAsync(
        string userId, string sessionKey, CancellationToken ct = default);

    /// <summary>RF-010: effective prefs (threshold clamped 5–1440, default 30).</summary>
    Task<HibernationPrefsDto> GetPrefsAsync(string userId, CancellationToken ct = default);

    /// <summary>RF-010: persist prefs; threshold applies on the next tick.</summary>
    Task<HibernationPrefsDto> SavePrefsAsync(
        string userId, HibernationPrefsDto prefs, CancellationToken ct = default);

    /// <summary>RF-009: pin/unpin — pinned sessions never hibernate.</summary>
    Task<bool> SetPinnedAsync(
        string userId, string sessionKey, bool pinned, CancellationToken ct = default);

    /// <summary>P3: hibernation stats — hibernated/pinned counts, total
    /// auto-resumed, estimated RSS saved (per-CLI heuristic).</summary>
    Task<HibernationStatsDto> GetStatsAsync(
        string userId, CancellationToken ct = default);
}
