namespace Taskboard.Agents;

/// <summary>
/// Sink único para eventos normalizados de execução de agentes
/// (SPEC-20260921-agent-execution-event-pipeline RF-003): atribui Sequence,
/// redige segredos, persiste e transmite — nunca lança para o produtor.
/// </summary>
public interface IAgentExecutionEventSink
{
    /// <summary>Sequencia, redige, persiste e transmite o evento. Retorna o evento com Sequence/EventId finais.</summary>
    Task<AgentExecutionEvent> EmitAsync(AgentExecutionEvent evt, CancellationToken cancellationToken = default);

    /// <summary>Página de replay: eventos com Sequence &gt; <paramref name="after"/>.</summary>
    Task<IReadOnlyList<AgentExecutionEvent>> GetEventsAsync(
        string scopeKind, string scopeId, long after = 0, int take = 500, CancellationToken cancellationToken = default);
}
