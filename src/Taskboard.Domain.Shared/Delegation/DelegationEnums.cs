namespace Taskboard.Delegation;

/// <summary>Lifecycle of a delegated task (SPEC-20261005 RF-001).</summary>
public enum DelegationTaskStatus
{
    Pending,
    Ready,
    Running,
    Done,
    Failed,
    Stale,
    Cancelled,
}

/// <summary>Message kinds of the agent mailbox (SPEC-20261005 RF-002).</summary>
public static class AgentMailboxKinds
{
    public const string Text = "text";
    public const string WorkerDone = "worker_done";
    public const string Heartbeat = "heartbeat";
    public const string Escalation = "escalation";

    /// <summary>P3 decision gate — reserved now so persisted kinds stay stable.</summary>
    public const string Decision = "decision";

    /// <summary>Kinds a chat tool may write — system kinds are dispatcher-only.</summary>
    public static readonly IReadOnlySet<string> AgentWritable =
        new HashSet<string>(StringComparer.Ordinal) { Text };
}

/// <summary>
/// SPEC-20261009 RF-001: what a delegation task does when dispatched.
/// <c>task</c> runs the prompt on its CLI; <c>coordinate</c> runs a planner CLI
/// and materializes the returned plan as child tasks in the same scope.
/// </summary>
public static class DelegationTaskKinds
{
    public const string Task = "task";
    public const string Coordinate = "coordinate";
}
