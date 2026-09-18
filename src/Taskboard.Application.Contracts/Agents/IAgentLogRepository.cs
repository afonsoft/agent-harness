namespace Taskboard.Agents;

/// <summary>
/// Persistência e recuperação de mensagens de log de agentes.
/// </summary>
public interface IAgentLogRepository
{
    /// <summary>
    /// Adiciona uma mensagem de log ao banco.
    /// </summary>
    Task AppendAsync(AgentLogMessage log, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recupera as mensagens de log de uma issue, ordenadas por Timestamp.
    /// </summary>
    Task<IReadOnlyList<AgentLogMessage>> GetByIssueIdAsync(string issueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove todas as mensagens de log de uma issue.
    /// </summary>
    Task DeleteByIssueIdAsync(string issueId, CancellationToken cancellationToken = default);
}
