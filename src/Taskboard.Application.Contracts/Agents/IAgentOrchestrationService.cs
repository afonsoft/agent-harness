using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Agents;

/// <summary>
/// Orquestra a execução dos agentes CLI em background e mantém o histórico de logs.
/// </summary>
public interface IAgentOrchestrationService
{
    /// <summary>
    /// Retorna os agentes elegíveis (instalados, autenticados e habilitados),
    /// marcando como ocupados os que estão em execução.
    /// </summary>
    Task<IReadOnlyList<AgentInfo>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Coloca uma nova requisição na fila de execução.
    /// Retorna <c>false</c> quando o agente não é elegível (desabilitado ou CLI não autenticado).
    /// </summary>
    Task<bool> EnqueueAsync(AgentExecutionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna o histórico de logs de uma issue.
    /// </summary>
    Task<IReadOnlyList<AgentLogMessage>> GetLogsAsync(string issueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove o histórico de logs de uma issue (memória e persistido).
    /// </summary>
    Task ClearLogsAsync(string issueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Solicita o cancelamento da execução de uma issue.
    /// Retorna <c>false</c> quando não há execução ativa para a issue.
    /// </summary>
    Task<bool> CancelAsync(string issueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna os runs mais recentes de uma issue (mais recente primeiro).
    /// </summary>
    Task<IReadOnlyList<AgentRunDto>> GetRunsAsync(string issueId, int take = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna o run mais recente de cada issue — usado para badges do board.
    /// </summary>
    Task<IReadOnlyList<AgentRunDto>> GetLatestRunsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Snapshot dos runIds vivos (enfileirados + em execução) neste processo —
    /// o reaper de runs stale nunca toca nesses ids
    /// (SPEC-20260920-harness-maintenance-jobs RF-002).
    /// </summary>
    IReadOnlyCollection<Guid> GetLiveRunIds();
}
