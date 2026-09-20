namespace Taskboard.Harness;

/// <summary>
/// Specialized role a pipeline stage plays
/// (SPEC-20260919-ade-multi-agent-orchestration RF-001).
/// </summary>
public enum AgentRole
{
    /// <summary>Produces the spec/plan consumed downstream.</summary>
    Architect,

    /// <summary>Writes implementation code into the shared run worktree.</summary>
    Builder,

    /// <summary>Authors/extends automated tests in the shared worktree.</summary>
    Tester,

    /// <summary>Reviews the worktree diff for quality/security conformity.</summary>
    Reviewer,
}
