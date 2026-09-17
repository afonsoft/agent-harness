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

    private AgentRun()
    {
    }

    public AgentRun(Guid id, string issueId, AgentType agentType, DateTimeOffset startedAt)
        : base(id)
    {
        IssueId = issueId;
        AgentType = agentType;
        State = AgentRunState.Queued;
        StartedAt = startedAt;
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
