namespace Taskboard.Agents;

/// <summary>
/// Persistence for normalized <see cref="AgentExecutionEvent"/>s (SPEC-20260921-agent-execution-event-pipeline).
/// </summary>
public interface IAgentRunEventRepository
{
    /// <summary>Persists an already-sequenced event.</summary>
    Task AppendAsync(AgentExecutionEvent evt, CancellationToken cancellationToken = default);

    /// <summary>Highest persisted <c>Sequence</c> for the scope (seeds the sequencer after restart).</summary>
    Task<long> GetMaxSequenceAsync(string scopeKind, string scopeId, CancellationToken cancellationToken = default);

    /// <summary>Page of events with <c>Sequence &gt; after</c>, ordered by Sequence.</summary>
    Task<IReadOnlyList<AgentExecutionEvent>> GetPageAsync(
        string scopeKind, string scopeId, long after, int take, CancellationToken cancellationToken = default);

    /// <summary>Deletes events older than the cutoff (retention).</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
}
