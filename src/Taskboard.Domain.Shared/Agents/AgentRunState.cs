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
    Canceled,

    /// <summary>Interrompido por teto de orçamento (SPEC-20260919-ade-observability-finops RF-003).</summary>
    BudgetExceeded
}
