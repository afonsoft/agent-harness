namespace Taskboard.Agents;

/// <summary>
/// SPEC-20261104-run-terminal-runtime RF-001: lifecycle of a delegated run's
/// persistent PTY — <see cref="Live"/> while the process is up,
/// <see cref="Finished"/> after exit/kill (archived output stays readable).
/// </summary>
public enum RunTerminalState
{
    Live,
    Finished,
}

/// <summary>Who is allowed to drive the run terminal's input (RF-007).</summary>
public enum RunTerminalControlOwner
{
    /// <summary>The orchestrator (run_send and friends).</summary>
    Operator,

    /// <summary>A human viewer typing in an attached terminal.</summary>
    Human,
}

/// <summary>Why a <c>run_wait</c> returned (RF-005).</summary>
public enum RunWaitReason
{
    /// <summary>The attention state matched the requested state.</summary>
    State,

    /// <summary>Post-registration output matched the requested regex.</summary>
    Output,

    /// <summary>A mailbox delivery interrupted the wait — it arrives as the caller's next inbox item.</summary>
    Message,

    /// <summary>Elapsed without a match — a check-in, not a failure.</summary>
    Timeout,

    /// <summary>The terminal process exited while waiting.</summary>
    Exited,
}

/// <summary>
/// SPEC-20261104-run-terminal-runtime RF-009: structured error kinds shared by
/// every run-surface tool — report what was observed, never what was assumed.
/// </summary>
public static class RunTerminalErrorKinds
{
    public const string NotFound = "not_found";
    public const string NeedsTarget = "needs_target";
    public const string Blocked = "blocked";
    public const string StatusMismatch = "status_mismatch";
    public const string Finished = "finished";
    public const string NotStarted = "not_started";
    public const string DeliveryFailed = "delivery_failed";
    public const string Unsupported = "unsupported";
    public const string Timeout = "timeout";
}
