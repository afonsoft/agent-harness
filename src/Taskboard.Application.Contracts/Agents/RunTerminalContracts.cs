using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261104-run-terminal-runtime RF-001: snapshot of a run terminal —
/// durable control state + live facts (status comes from the attention
/// store, liveness from the PTY registry).
/// </summary>
public sealed record RunTerminalDto(
    string Id,
    string TaskId,
    string? DispatchId,
    string PtySessionId,
    string CliName,
    RunTerminalState State,
    RunTerminalControlOwner ControlOwner,
    /// <summary>Current attention state of <c>run:{taskId}</c>, when known.</summary>
    string? Status,
    DateTime CreatedAtUtc,
    DateTime? FinishedAtUtc,
    int? ExitCode,
    /// <summary>First-message delivery stamp of the bound dispatch (RF-002).</summary>
    bool FirstMessageSent,
    string? FirstMessageReason,
    /// <summary>Scrollback tail captured at finish — populated on finished rows.</summary>
    string? ArchivedOutput = null);

/// <summary>Spawn parameters for a new run terminal.</summary>
public sealed record EnsureRunTerminalRequest(
    string TaskId,
    string? DispatchId,
    /// <summary>CLI display/binary name — drives interrupt sequence + cli column.</summary>
    string CliName,
    /// <summary>Launch argv (TUI mode — never a one-shot print form).</summary>
    IReadOnlyList<string> Command,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment,
    /// <summary>Scope that owns the task — binds mailbox waits and scope guards.</summary>
    string? Scope = null);

/// <summary>Structured run-surface error (RF-009).</summary>
public sealed record RunTerminalError(string Kind, string Message, string? Hint = null);

/// <summary>
/// Outcome of a send/interrupt op — <see cref="Error"/> set on refusal.
/// <paramref name="Advanced"/> reports whether the turn visibly advanced
/// after a <c>--wait</c> send (null when no wait was requested).
/// </summary>
public sealed record RunTerminalOpResult(
    bool Delivered,
    RunTerminalDto? Terminal,
    RunTerminalError? Error = null,
    bool? Advanced = null);

/// <summary>RF-004: read result — screen snapshot or history tail.</summary>
public sealed record RunReadResult(
    string Target,
    /// <summary>Redacted text: viewport for screen reads, tail for history.</summary>
    string Text,
    /// <summary>History read degraded to screen while the agent is busy (RF-004).</summary>
    bool Incomplete,
    string? Why,
    RunTerminalDto? Terminal,
    RunTerminalError? Error = null);

/// <summary>RF-005: wait registration + outcome.</summary>
public sealed record RunWaitRequest(
    string Target,
    /// <summary>Attention state name to match (e.g. "idle", "working", "done").</summary>
    string? State = null,
    /// <summary>Regex matched only against output emitted after this call.</summary>
    string? Output = null,
    TimeSpan? Timeout = null);

public sealed record RunWaitResult(
    RunWaitReason Reason,
    string? State,
    string? Matched,
    RunTerminalDto? Terminal,
    RunTerminalError? Error = null);

/// <summary>Persistence boundary — the entity stays inside the EF layer.</summary>
public interface IRunTerminalRepository
{
    Task<RunTerminalDto?> GetAsync(string id, CancellationToken ct = default);
    Task<RunTerminalDto?> GetByDispatchAsync(string dispatchId, CancellationToken ct = default);
    Task<RunTerminalDto?> GetByPtySessionIdAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Active (non-finished) terminal of a task — at most one exists.</summary>
    Task<RunTerminalDto?> GetLiveByTaskAsync(string taskId, CancellationToken ct = default);
    Task<IReadOnlyList<RunTerminalDto>> ListByTaskAsync(string taskId, CancellationToken ct = default);
    Task<IReadOnlyList<RunTerminalDto>> ListLiveAsync(CancellationToken ct = default);

    Task<RunTerminalDto> AddAsync(
        EnsureRunTerminalRequest request, string id, string ptySessionId, CancellationToken ct = default);
    Task<RunTerminalDto?> SetControlOwnerAsync(
        string id, RunTerminalControlOwner owner, CancellationToken ct = default);
    Task<RunTerminalDto?> FinishAsync(
        string id, int? exitCode, string? archivedOutput, DateTime now, CancellationToken ct = default);
}

/// <summary>
/// SPEC-20261104-run-terminal-runtime: control plane over delegated run
/// terminals — spawn/reuse, steer (send/interrupt), observe (read/wait),
/// human attach bookkeeping. Tool and endpoint surfaces share the error
/// taxonomy of <see cref="RunTerminalErrorKinds"/>.
/// </summary>
public interface IRunTerminalService
{
    /// <summary>
    /// Returns the live terminal of <paramref name="request"/>'s dispatch or
    /// spawns one — idempotent for dispatch retries (same deterministic PTY
    /// id, so a live session is reused, never duplicated).
    /// </summary>
    Task<RunTerminalDto> EnsureAsync(EnsureRunTerminalRequest request, CancellationToken ct = default);

    /// <summary>Terminal lookup by run-terminal id, dispatch id, task id or PTY session id.</summary>
    Task<RunTerminalDto?> GetAsync(string target, string? scope = null, CancellationToken ct = default);

    /// <summary>
    /// RF-003: writes <paramref name="text"/> + Enter as PTY input. Refuses
    /// while the attention status is blocked/waiting_you unless
    /// <paramref name="force"/>; warns (status_mismatch) while a human drives.
    /// </summary>
    Task<RunTerminalOpResult> SendAsync(
        string target, string text, bool wait = false, bool force = false,
        string? scope = null, CancellationToken ct = default);

    /// <summary>RF-004: screen snapshot, or history tail (≤64KB) when not busy.</summary>
    Task<RunReadResult> ReadAsync(
        string target, int? lines = null, bool history = false,
        string? scope = null, CancellationToken ct = default);

    /// <summary>RF-005: waits on state transition / post-registration output / mailbox / timeout.</summary>
    Task<RunWaitResult> WaitAsync(RunWaitRequest request, string? scope = null, CancellationToken ct = default);

    /// <summary>RF-006: interrupt sequence (Ctrl+C/Esc per CLI) — turn stop, never a kill.</summary>
    Task<RunTerminalOpResult> InterruptAsync(string target, string? scope = null, CancellationToken ct = default);

    /// <summary>
    /// SPEC-20261104-agent-lineage-notes RF-002: kills the PTY behind a live
    /// terminal (lineage cascade). Refused for human-controlled terminals —
    /// they reparent instead of dying with the parent.
    /// </summary>
    Task<RunTerminalOpResult> KillAsync(string target, string? scope = null, CancellationToken ct = default);

    /// <summary>
    /// RF-002: records the first-message delivery on the bound dispatch —
    /// durable, inspectable after restart; idempotent (first record wins).
    /// </summary>
    Task RecordFirstMessageAsync(string dispatchId, bool sent, string? reason, CancellationToken ct = default);

    /// <summary>Internal hook: PTY exited — finish the row, resolve waits, archive the tail.</summary>
    Task OnSessionExitedAsync(
        string sessionId, int exitCode, string scrollbackTail, CancellationToken ct = default);

    /// <summary>Internal hook: a human viewer typed on the PTY (RF-007).</summary>
    Task OnHumanInputAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Internal hook: every human viewer detached (RF-007).</summary>
    Task OnViewersEmptyAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Viewer attach for the Terminal page — replays scrollback to the new
    /// viewer; returns the PTY session id on success, null when the target
    /// has no live run terminal.
    /// </summary>
    Task<string?> AttachViewerAsync(
        string target, string connectionId, string userKey,
        Func<string, string, Task> onOutput, Func<string, string, Task> onClosed,
        CancellationToken ct = default);

    /// <summary>Viewer detach (tab close / connection drop).</summary>
    Task DetachViewerAsync(string sessionId, string connectionId, CancellationToken ct = default);

    /// <summary>Releases the runtime pin — the PTY returns to normal idle reaping.</summary>
    Task UnpinAsync(string sessionId, CancellationToken ct = default);
}
