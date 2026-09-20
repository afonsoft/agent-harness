namespace Taskboard.Harness;

/// <summary>
/// Lifecycle of a single pipeline stage execution
/// (SPEC-20260919-ade-multi-agent-orchestration §2).
/// </summary>
public enum StageStatus
{
    /// <summary>Waiting for its `DependsOn` stages to complete.</summary>
    Pending,

    /// <summary>Eligible and currently executing (agent dispatched or automated step).</summary>
    Running,

    /// <summary>Work done; holding on a human-approval gate.</summary>
    WaitingApproval,

    /// <summary>Finished successfully.</summary>
    Completed,

    /// <summary>Failed; pipeline paused — retryable from this point.</summary>
    Failed,

    /// <summary>Not executed (e.g. pipeline cancelled or stage short-circuited).</summary>
    Skipped,
}
