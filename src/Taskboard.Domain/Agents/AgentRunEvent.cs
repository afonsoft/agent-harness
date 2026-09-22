using Taskboard.Agents;

namespace Taskboard.Domain.Agents;

/// <summary>
/// Normalized persisted event of a CLI agent execution
/// (SPEC-20260921-agent-execution-event-pipeline RF-003). Replay source for
/// Board, Cockpit and AI Chat.
/// </summary>
public sealed class AgentRunEvent : Entity<Guid>
{
    public string ScopeKind { get; private set; } = string.Empty;

    public string ScopeId { get; private set; } = string.Empty;

    /// <summary>Monotonic sequence per (ScopeKind, ScopeId) — arrival order.</summary>
    public long Sequence { get; private set; }

    public string Kind { get; private set; } = string.Empty;

    public string? StageId { get; private set; }

    public string? SessionId { get; private set; }

    public string? ParentEventId { get; private set; }

    public string? ToolCallId { get; private set; }

    /// <summary>ACP v2 message correlation id (message upserts).</summary>
    public string? MessageId { get; private set; }

    /// <summary>ACP v2 plan correlation id (plan_update).</summary>
    public string? PlanId { get; private set; }

    /// <summary>Upsert merge hint — append (default/null), replace or clear.</summary>
    public string? PatchOp { get; private set; }

    public string? Title { get; private set; }

    public string? PayloadJson { get; private set; }

    /// <summary>Original transport message (truncated, redacted). Faithful debug/replay.</summary>
    public string? RawJson { get; private set; }

    public string Stream { get; private set; } = "system";

    public DateTimeOffset TimestampUtc { get; private set; }

    private AgentRunEvent()
    {
    }

    public AgentRunEvent(
        Guid id,
        string scopeKind,
        string scopeId,
        long sequence,
        string kind,
        DateTimeOffset timestampUtc,
        string? stageId = null,
        string? sessionId = null,
        string? parentEventId = null,
        string? toolCallId = null,
        string? title = null,
        string? payloadJson = null,
        string? rawJson = null,
        string stream = "system",
        string? messageId = null,
        string? planId = null,
        string? patchOp = null)
        : base(id)
    {
        ScopeKind = scopeKind;
        ScopeId = scopeId;
        Sequence = sequence;
        Kind = kind;
        StageId = stageId;
        SessionId = sessionId;
        ParentEventId = parentEventId;
        ToolCallId = toolCallId;
        Title = title;
        PayloadJson = payloadJson;
        RawJson = rawJson;
        Stream = stream;
        TimestampUtc = timestampUtc;
        MessageId = messageId;
        PlanId = planId;
        PatchOp = patchOp;
    }

    public static AgentRunEvent From(AgentExecutionEvent evt) => new(
        Guid.TryParse(evt.EventId, out var eventId) ? eventId : Guid.NewGuid(),
        evt.ScopeKind,
        evt.ScopeId,
        evt.Sequence,
        evt.Kind,
        evt.TimestampUtc,
        evt.StageId,
        evt.SessionId,
        evt.ParentEventId,
        evt.ToolCallId,
        evt.Title,
        evt.PayloadJson,
        evt.RawJson,
        evt.Stream,
        evt.MessageId,
        evt.PlanId,
        evt.PatchOp);

    public AgentExecutionEvent ToEvent() => new(
        Id.ToString("N"),
        ScopeKind,
        ScopeId,
        Sequence,
        TimestampUtc,
        Kind,
        StageId,
        SessionId,
        ParentEventId,
        ToolCallId,
        Title,
        PayloadJson,
        RawJson,
        Stream,
        MessageId,
        PlanId,
        PatchOp);
}
