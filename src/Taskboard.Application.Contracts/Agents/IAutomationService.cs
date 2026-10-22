using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261021-agent-automations RF-006: CRUD + manual fire + run history
/// for scheduled agent prompts. The AutomationDispatcher (hosted service)
/// is the fire path; this service owns validation and persistence.
/// </summary>
public interface IAutomationService
{
    Task<AgentAutomationDto> CreateAsync(CreateAutomationRequest request, CancellationToken ct = default);
    Task<AgentAutomationDto?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentAutomationDto>> ListAsync(CancellationToken ct = default);
    Task<AgentAutomationDto?> UpdateAsync(string id, UpdateAutomationRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    /// <summary>Manual fire — creates a run immediately (precheck still applies).</summary>
    Task<AgentAutomationRunDto?> RunNowAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentAutomationRunDto>> ListRunsAsync(string id, int take = 50, CancellationToken ct = default);
}
