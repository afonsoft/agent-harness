using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Live model catalog reported by the installed CLI itself
/// (SPEC-20260918-agent-model-tiers — model dropdown in the CLI Agents
/// screen). Distinct from <see cref="IAgentModelConfigService"/>, which owns
/// per-tier overrides: this service only reads what the CLI can run.
/// </summary>
public interface IAgentModelCatalogService
{
    /// <summary>
    /// Model ids reported by the CLI's headless model-list command
    /// (e.g. <c>opencode models</c>, <c>devin models list</c>,
    /// <c>agy models</c>). Empty when the CLI has no documented probe, is not
    /// installed, or the probe fails/times out. Results are cached briefly;
    /// <paramref name="forceRefresh"/> skips the cache and re-runs the probe.
    /// </summary>
    Task<IReadOnlyList<string>> ListAvailableAsync(
        AgentType agentType, bool forceRefresh = false, CancellationToken cancellationToken = default);
}
