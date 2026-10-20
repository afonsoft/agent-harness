namespace Taskboard.Agents;

/// <summary>
/// SPEC-20261020-agent-attention-model RF-001: unified attention state of an
/// agent session — the single store every surface (board, badge, feed)
/// subscribes to. Distinct from <see cref="AgentRunState"/>: that is the
/// run lifecycle verdict, this is the live "does it need me?" state.
/// </summary>
public enum AgentAttentionState
{
    /// <summary>Producing output / heartbeat alive.</summary>
    Working,

    /// <summary>Blocked on the human: permission prompt, question, decision.</summary>
    WaitingYou,

    /// <summary>Finished successfully (worker_done / run succeeded).</summary>
    Done,

    /// <summary>No output for the idle window while the run is open.</summary>
    Idle,

    /// <summary>Failed/stale/escalated — terminal but needs triage.</summary>
    Blocked,

    /// <summary>Process exited without a completion signal.</summary>
    Exited,

    /// <summary>
    /// No signal for the unverifiable window — never infer death from
    /// silence (a remote/federated worker may just be unreachable).
    /// </summary>
    Unverifiable,
}

/// <summary>SPEC-20261020-agent-attention-model: provenance of an attention signal.</summary>
public enum AttentionSignalKind
{
    /// <summary>Managed hook installed in the CLI config (POST /api/agent-hooks).</summary>
    Hook,

    /// <summary>OSC title sequence captured from the PTY.</summary>
    Osc,

    /// <summary>Heuristic match on the output tail (permission/question pattern).</summary>
    OutputPattern,

    /// <summary>No output/heartbeat inside the idle window.</summary>
    Idle,

    /// <summary>Mailbox lifecycle message (decision/escalation/worker_done).</summary>
    Mailbox,

    /// <summary>Harness lifecycle transition (run/task/process state change).</summary>
    Lifecycle,
}
