using Taskboard.Agents;

namespace Taskboard.Domain.Agents;

/// <summary>
/// Registro persistido de uma execução de agente CLI sobre uma issue.
/// Metadados apenas — nunca contém instruções, credenciais ou conteúdo de log.
/// </summary>
public sealed class AgentRun : Entity<Guid>
{
    public string IssueId { get; private set; } = string.Empty;

    public AgentType AgentType { get; private set; }

    public AgentRunState State { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Tier de modelo pedido (SPEC-20260918-agent-model-tiers). Null = runs antigas.</summary>
    public AgentModelTier? ModelTier { get; private set; }

    /// <summary>Nome do modelo resolvido para a CLI; null quando gerenciado pela CLI.</summary>
    public string? ModelName { get; private set; }

    private AgentRun()
    {
    }

    public AgentRun(
        Guid id,
        string issueId,
        AgentType agentType,
        DateTimeOffset startedAt,
        AgentModelTier? modelTier = null,
        string? modelName = null)
        : base(id)
    {
        IssueId = issueId;
        AgentType = agentType;
        State = AgentRunState.Queued;
        StartedAt = startedAt;
        ModelTier = modelTier;
        ModelName = modelName;
    }

    public void MarkRunning()
    {
        if (State == AgentRunState.Queued)
        {
            State = AgentRunState.Running;
        }
    }

    public void MarkFinished(AgentRunState finalState, DateTimeOffset finishedAt)
    {
        if (finalState is AgentRunState.Queued or AgentRunState.Running)
        {
            throw new ArgumentException("Final state must be Succeeded, Failed or Canceled.", nameof(finalState));
        }

        State = finalState;
        FinishedAt = finishedAt;
    }
}
