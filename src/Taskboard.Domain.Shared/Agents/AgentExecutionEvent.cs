namespace Taskboard.Agents;

/// <summary>
/// Normalized execution event emitted by every agent CLI transport
/// (ACP session, one-shot JSON-RPC, raw stdout) so Board, Cockpit and
/// AI Chat consume a single event model — SPEC-20260921-agent-execution-event-pipeline.
/// </summary>
public sealed record AgentExecutionEvent(
    string EventId,
    string ScopeKind,
    string ScopeId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Kind,
    string? StageId = null,
    string? SessionId = null,
    string? ParentEventId = null,
    string? ToolCallId = null,
    string? Title = null,
    string? PayloadJson = null,
    string? RawJson = null,
    string Stream = "system",
    string? MessageId = null,
    string? PlanId = null,
    string? PatchOp = null);

/// <summary>Scope an <see cref="AgentExecutionEvent"/> belongs to.</summary>
public static class AgentEventScope
{
    public const string Run = "run";
    public const string Thread = "thread";
    public const string Issue = "issue";
}

/// <summary>Taxonomy of <see cref="AgentExecutionEvent.Kind"/>.</summary>
public static class AgentEventKinds
{
    public const string Lifecycle = "lifecycle";
    public const string Message = "message";
    public const string Thought = "thought";
    public const string Plan = "plan";
    public const string ToolCall = "tool_call";
    public const string ToolOutput = "tool_output";
    public const string Permission = "permission";
    public const string Output = "output";
    public const string Diff = "diff";
    public const string Verification = "verification";
    public const string Metric = "metric";
    public const string Error = "error";
    public const string Approval = "approval";
    public const string Steer = "steer";
    public const string Activity = "activity";

    /// <summary>Agent-advertised slash commands (ACP available_commands_update).</summary>
    public const string Commands = "commands";

    /// <summary>Session metadata: modes, config options, negotiated peer info.</summary>
    public const string SessionInfo = "session_info";

    /// <summary>Terminal display updates (ACP v2 terminal_update / terminal_output_chunk).</summary>
    public const string Terminal = "terminal";
}

/// <summary>
/// Upsert merge hint carried by <see cref="AgentExecutionEvent.PatchOp"/>
/// (SPEC-20260921-acp-v2-readiness RF-203). Null/absent behaves as
/// <see cref="Append"/> — v1 never emits anything else.
/// </summary>
public static class AgentPatchOps
{
    /// <summary>New row / chunk append (also the implicit default).</summary>
    public const string Append = "append";

    /// <summary>Field-level merge into the entity keyed by ToolCallId/MessageId/PlanId.</summary>
    public const string Replace = "replace";

    /// <summary>Clears the keyed entity's content (explicit null/empty).</summary>
    public const string Clear = "clear";
}
