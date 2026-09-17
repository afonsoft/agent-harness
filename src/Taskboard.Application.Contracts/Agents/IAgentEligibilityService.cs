using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Resolve quais <see cref="AgentType"/> podem executar trabalho agora:
/// CLI instalado + autenticado (<see cref="IAgentCliStatusService"/>) e habilitado
/// nas preferências. Tabela de preferências vazia = "first run" (todos elegíveis),
/// mesma convenção do <c>EnabledAgentResolver</c>.
/// (SPEC-20260917-agent-eligibility-task-badge RF-003)
/// </summary>
public interface IAgentEligibilityService
{
    Task<IReadOnlySet<AgentType>> GetEligibleTypesAsync(CancellationToken cancellationToken = default);
}
