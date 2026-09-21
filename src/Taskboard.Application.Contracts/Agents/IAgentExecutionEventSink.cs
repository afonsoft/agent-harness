namespace Taskboard.Agents;

/// <summary>
/// Single sink for normalized agent execution events
/// (SPEC-20260921-agent-execution-event-pipeline RF-003): assigns Sequence,
/// redacts secrets, persists and broadcasts — never throws at the producer.
/// </summary>
public interface IAgentExecutionEventSink
{
    /// <summary>Sequences, redacts, persists and broadcasts the event. Returns the event with final Sequence/EventId.</summary>
    Task<AgentExecutionEvent> EmitAsync(AgentExecutionEvent evt, CancellationToken cancellationToken = default);

    /// <summary>Replay page: events with Sequence &gt; <paramref name="after"/>.</summary>
    Task<IReadOnlyList<AgentExecutionEvent>> GetEventsAsync(
        string scopeKind, string scopeId, long after = 0, int take = 500, CancellationToken cancellationToken = default);
}
