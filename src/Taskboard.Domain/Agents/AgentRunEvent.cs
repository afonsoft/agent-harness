using Taskboard.Agents;

namespace Taskboard.Domain.Agents;

/// <summary>
/// Evento normalizado e persistido de uma execução de agente CLI
/// (SPEC-20260921-agent-execution-event-pipeline RF-003). Fonte de replay
/// para Board, Cockpit e AI Chat.
/// </summary>
public sealed class AgentRunEvent : Entity<Guid>
{
    public string ScopeKind { get; private set; } = string.Empty;

    public string ScopeId { get; private set; } = string.Empty;

    /// <summary>Sequência monotônica por (ScopeKind, ScopeId) — ordem de chegada.</summary>
    public long Sequence { get; private set; }

    public string Kind { get; private set; } = string.Empty;

    public string? StageId { get; private set; }

    public string? SessionId { get; private set; }

    public string? ParentEventId { get; private set; }

    public string? ToolCallId { get; private set; }

    public string? Title { get; private set; }

    public string? PayloadJson { get; private set; }

    /// <summary>Mensagem original do transporte (truncada, redacted). Debug/replay fiel.</summary>
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
        string stream = "system")
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
    }

    public static AgentRunEvent From(AgentExecutionEvent evt) => new(
        Guid.NewGuid(),
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
        evt.Stream);

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
        Stream);
}
