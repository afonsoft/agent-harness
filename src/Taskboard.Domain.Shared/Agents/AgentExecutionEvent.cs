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
    string Stream = "system");

/// <summary>Escopo ao qual um <see cref="AgentExecutionEvent"/> pertence.</summary>
public static class AgentEventScope
{
    public const string Run = "run";
    public const string Thread = "thread";
    public const string Issue = "issue";
}

/// <summary>Taxonomia de <see cref="AgentExecutionEvent.Kind"/>.</summary>
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
}
