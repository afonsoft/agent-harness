namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Detects installation and authentication state of the supported agent CLIs
/// (SPEC-20260917-cli-agents-terminal). Never runs interactive commands —
/// only PATH lookup, bounded <c>--version</c> probes and credential-file
/// existence checks.
/// </summary>
public interface IAgentCliStatusService
{
    Task<IReadOnlyList<AgentCliStatus>> GetStatusAsync(CancellationToken cancellationToken = default);
}
