using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Persistência e recuperação de execuções de agentes por issue.
/// </summary>
public interface IAgentRunRepository
{
    /// <summary>Cria um run no estado <see cref="AgentRunState.Queued"/> e retorna seu DTO.</summary>
    Task<AgentRunDto> EnqueueAsync(string issueId, AgentType agentType, CancellationToken cancellationToken = default);

    /// <summary>Transiciona o run para <see cref="AgentRunState.Running"/> (idempotente).</summary>
    Task MarkRunningAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Transiciona o run para um estado terminal com timestamp de fim.</summary>
    Task FinishAsync(Guid runId, AgentRunState finalState, CancellationToken cancellationToken = default);

    /// <summary>Últimos runs de uma issue, mais recente primeiro.</summary>
    Task<IReadOnlyList<AgentRunDto>> GetByIssueIdAsync(string issueId, int take = 5, CancellationToken cancellationToken = default);

    /// <summary>Run mais recente por issue (para badges do board — cobre ativos e finalizados).</summary>
    Task<IReadOnlyList<AgentRunDto>> GetLatestPerIssueAsync(CancellationToken cancellationToken = default);
}
