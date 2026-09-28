using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Persistence port for user-declared agent CLIs — returns DTOs, never
/// entities (keeps <c>Integrations</c> decoupled from <c>Domain</c>).
/// SPEC-20260928-ai-code-generic-cli RF-002.
/// </summary>
public interface IAgentCliDefinitionRepository
{
    Task<IReadOnlyList<AgentCliDefinitionDto>> ListAsync(CancellationToken ct = default);
    Task<AgentCliDefinitionDto?> GetAsync(string id, CancellationToken ct = default);
    Task<bool> DisplayNameExistsAsync(string displayName, CancellationToken ct = default);
    Task<AgentCliDefinitionDto> AddAsync(UpsertAgentCliDefinitionRequest request, CancellationToken ct = default);
    Task<AgentCliDefinitionDto?> UpdateAsync(string id, UpsertAgentCliDefinitionRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
