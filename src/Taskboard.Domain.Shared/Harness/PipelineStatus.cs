namespace Taskboard.Harness;

/// <summary>
/// Aggregate status of a pipeline execution
/// (SPEC-20260919-ade-multi-agent-orchestration RF-005).
/// </summary>
public enum PipelineStatus
{
    /// <summary>At least one stage is running or eligible.</summary>
    Running,

    /// <summary>Blocked on a human-approval gate.</summary>
    WaitingApproval,

    /// <summary>A stage failed; the pipeline is paused until retry/cancel.</summary>
    AwaitingRetry,

    /// <summary>All stages completed successfully.</summary>
    Completed,

    /// <summary>Cancelled by the user; running stages were shut down safely.</summary>
    Cancelled,
}
