namespace Taskboard.Harness;

/// <summary>
/// What a pipeline stage does when it becomes eligible
/// (SPEC-20260919-ade-multi-agent-orchestration §2 gates).
/// </summary>
public enum PipelineStageKind
{
    /// <summary>Dispatched to a CLI agent with a role + model tier.</summary>
    AgentWork,

    /// <summary>Holds the pipeline in `WaitingApproval` until a human approves.</summary>
    Approval,

    /// <summary>Runs the deterministic verification loop (build/test/coverage).</summary>
    Verification,
}
