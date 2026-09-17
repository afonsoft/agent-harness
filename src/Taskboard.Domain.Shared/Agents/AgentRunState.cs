namespace Taskboard.Agents;

/// <summary>
/// Estado de uma execução de agente CLI sobre uma issue/tarefa.
/// </summary>
public enum AgentRunState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Canceled
}
