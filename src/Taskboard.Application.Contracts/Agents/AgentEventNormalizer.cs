using System.Text.Json;

namespace Taskboard.Agents;

/// <summary>
/// Converts raw transport messages (one-shot stdout/stderr, opportunistic
/// JSON-RPC) into normalized <see cref="AgentExecutionEvent"/>s.
/// Stateless — sequencing/redaction happen in the sink.
/// </summary>
public static class AgentEventNormalizer
{
    /// <summary>
    /// Normalizes an <see cref="AgentLogMessage"/>: when the transport already
    /// filled <see cref="AgentLogMessage.Kind"/> (e.g. a parsed JSON-RPC
    /// notification), it is preserved; otherwise an <c>output</c> event is
    /// emitted carrying the raw text in the payload so replay stays faithful.
    /// </summary>
    public static AgentExecutionEvent FromLogMessage(
        AgentLogMessage message,
        string scopeKind,
        string scopeId,
        string? stageId = null,
        string? sessionId = null)
    {
        var stream = message.Stream switch
        {
            AgentLogStream.StdOut => "stdout",
            AgentLogStream.StdErr => "stderr",
            _ => "system"
        };

        var payloadJson = message.PayloadJson
            ?? (string.IsNullOrEmpty(message.Content)
                ? null
                : JsonSerializer.Serialize(new { line = message.Content }));

        return new AgentExecutionEvent(
            EventId: string.Empty, // assigned by the sink
            ScopeKind: scopeKind,
            ScopeId: scopeId,
            Sequence: 0,
            TimestampUtc: message.Timestamp,
            Kind: message.Kind ?? AgentEventKinds.Output,
            StageId: stageId,
            SessionId: sessionId,
            Title: null,
            PayloadJson: payloadJson,
            RawJson: null,
            Stream: stream);
    }
}
