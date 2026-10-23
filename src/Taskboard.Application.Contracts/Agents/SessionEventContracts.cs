using System.Text.Json;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261022-session-event-log RF-001: durable append-only session facts.
/// Only durable settlements are logged — transient stream frames are excluded.
/// </summary>
public enum SessionEventKind
{
    RunStarted = 0,
    UserMessage = 1,
    AssistantMessage = 2,
    ToolCall = 3,
    ToolResult = 4,
    StepBoundary = 5,
    RunCompleted = 6,
    RunFailed = 7,
    RunCanceled = 8
}

/// <summary>
/// One durable fact in a session's event log. <see cref="Payload"/> is a
/// kind-specific JSON document; producers are responsible for secret
/// scrubbing before append (organization guardrail).
/// </summary>
public sealed record SessionEvent(
    int Generation,
    long Seq,
    DateTimeOffset TimestampUtc,
    SessionEventKind Kind,
    JsonElement Payload);

/// <summary>Thrown when a persisted event carries a generation newer than
/// the newest supported one — refused, never silently parsed (RF-002).</summary>
public sealed class UnknownSessionEventGenerationException : Exception
{
    public UnknownSessionEventGenerationException(int found, int supported)
        : base($"Session event generation {found} is newer than supported generation {supported}.")
    {
        Found = found;
        Supported = supported;
    }

    public int Found { get; }
    public int Supported { get; }
}

/// <summary>Result of opening a session event log for read.</summary>
public sealed record SessionEventReadResult(
    string SessionId,
    IReadOnlyList<SessionEvent> Events,
    bool TruncatedTail);

/// <summary>
/// SPEC-20261022-session-event-log RF-001/RF-002: append-only, generation-
/// versioned per-session event log (JSONL under the harness data dir).
/// </summary>
public interface ISessionEventLog
{
    /// <summary>Appends one event; assigns Seq monotonically per session.
    /// Payload must already be secret-scrubbed by the producer.</summary>
    Task<SessionEvent> AppendAsync(
        string sessionId,
        SessionEventKind kind,
        JsonElement payload,
        CancellationToken ct = default);

    /// <summary>Reads all events, migrating older supported generations
    /// forward. Flags (never silently drops) a truncated tail line.</summary>
    Task<SessionEventReadResult> ReadAsync(string sessionId, CancellationToken ct = default);
}
