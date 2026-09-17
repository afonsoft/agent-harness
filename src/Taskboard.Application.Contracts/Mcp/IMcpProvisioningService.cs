using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Mcp;

/// <summary>
/// Provisions the configured RAG MCP server (<c>Taskboard:Rag:*</c>) into the
/// user-scope config file of every enabled agent CLI, and removes it when the
/// URL is cleared (SPEC-20260917-rag-mcp-provisioning).
/// </summary>
public interface IMcpProvisioningService
{
    /// <summary>
    /// Returns the current snapshot: in-flight/last run state plus a live
    /// per-agent re-read of the config files.
    /// </summary>
    McpProvisionStatus GetStatus();

    /// <summary>
    /// Starts a provision pass in the background for <paramref name="agents"/>
    /// (or all enabled agents when null). Concurrent requests coalesce.
    /// </summary>
    void RequestProvision(IReadOnlyCollection<AgentType>? agents = null);

    /// <summary>
    /// Runs the provision pass synchronously. When a run is in flight, returns
    /// the current status snapshot instead of duplicating work.
    /// </summary>
    Task<McpProvisionStatus> ProvisionAsync(
        IReadOnlyCollection<AgentType>? agents = null,
        CancellationToken cancellationToken = default);
}
