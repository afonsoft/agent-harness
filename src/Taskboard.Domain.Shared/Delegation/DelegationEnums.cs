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

    /// <summary>SPEC-20261020-v2 RF-005: held by an open gate until resolved.</summary>
    Blocked,
}

/// <summary>Lifecycle of a coordination run (SPEC-20261020-orchestration-contract-v2 RF-001).</summary>
public enum DelegationRunStatus
{
    Open,
    Settled,
    Aborted,
}

/// <summary>
/// Lifecycle of a dispatch — one attempt of a delegation task
/// (SPEC-20261020-orchestration-contract-v2 RF-002).
/// </summary>
public enum DispatchState
{
    Pending,
    Dispatched,
    Settled,
    Released,
    Retained,
    Abandoned,
}

/// <summary>Message kinds of the agent mailbox (SPEC-20261005 RF-002).</summary>
public static class AgentMailboxKinds
{
    public const string Text = "text";
    public const string WorkerDone = "worker_done";
    public const string Heartbeat = "heartbeat";
    public const string Escalation = "escalation";

    /// <summary>SPEC-20261022-task-prompt-refiner RF-008: prompt-refinement
    /// diagnostics event (mode, anchors found, byte delta).</summary>
    public const string RefinedPrompt = "refined_prompt";

    /// <summary>P3 decision gate — reserved now so persisted kinds stay stable.</summary>
    public const string Decision = "decision";

    /// <summary>SPEC-20261020-v2 RF-004: worker → coordinator blocking question.</summary>
    public const string Question = "question";

    /// <summary>SPEC-20261020-v2 RF-004: coordinator → worker answer.</summary>
    public const string Answer = "answer";

    /// <summary>Kinds a chat tool may write — system kinds are dispatcher-only.</summary>
    public static readonly IReadOnlySet<string> AgentWritable =
        new HashSet<string>(StringComparer.Ordinal) { Text };
}

/// <summary>Lifecycle of a worker question (SPEC-20261020-v2 RF-004).</summary>
public enum PendingQuestionStatus
{
    Pending,
    Answered,
    TimedOut,
}

/// <summary>Lifecycle of a task-bound gate (SPEC-20261020-v2 RF-005).</summary>
public enum DelegationGateStatus
{
    Open,
    Resolved,
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
