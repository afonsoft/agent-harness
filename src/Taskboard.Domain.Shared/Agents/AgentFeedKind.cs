namespace Taskboard.Agents;

/// <summary>
/// SPEC-20261020-agents-feed-notifications RF-001/RF-006: feed categories —
/// notification preferences key on these values.
/// </summary>
public enum AgentFeedKind
{
    /// <summary>Run/task completed (worker_done, attention Done).</summary>
    Completion,

    /// <summary>Needs the human: permission prompt, waiting_you.</summary>
    Attention,

    /// <summary>Question/decision posted or resolved.</summary>
    Question,

    /// <summary>Failed/stale/escalated/abandoned — needs triage.</summary>
    Failure,

    /// <summary>Worktree status/comment checkpoint.</summary>
    Worktree,

    /// <summary>PR/MR state change of the selected repo.</summary>
    Pr,

    /// <summary>Session transcript removed from CLI history (SPEC session-history-parity RF-008).</summary>
    SessionDeleted,

    /// <summary>Done/idle session killed for hibernation (SPEC agent-hibernation RF-002).</summary>
    SessionHibernated,

    /// <summary>Hibernated session respawned via native resume (SPEC agent-hibernation RF-007).</summary>
    SessionResumed,
}
