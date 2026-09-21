namespace Taskboard.Agents;

/// <summary>
/// Persistência de <see cref="AgentExecutionEvent"/> normalizados (SPEC-20260921-agent-execution-event-pipeline).
/// </summary>
public interface IAgentRunEventRepository
{
    /// <summary>Persiste um evento já sequenciado.</summary>
    Task AppendAsync(AgentExecutionEvent evt, CancellationToken cancellationToken = default);

    /// <summary>Maior <c>Sequence</c> persistida para o escopo (seed do sequencer após restart).</summary>
    Task<long> GetMaxSequenceAsync(string scopeKind, string scopeId, CancellationToken cancellationToken = default);

    /// <summary>Página de eventos com <c>Sequence &gt; after</c>, ordenada por Sequence.</summary>
    Task<IReadOnlyList<AgentExecutionEvent>> GetPageAsync(
        string scopeKind, string scopeId, long after, int take, CancellationToken cancellationToken = default);

    /// <summary>Remove eventos anteriores ao cutoff (retenção).</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
}
