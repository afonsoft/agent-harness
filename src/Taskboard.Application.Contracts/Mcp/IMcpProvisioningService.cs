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
    /// Queues a provision pass in the background for <paramref name="agents"/>
    /// (or all enabled agents when null). Requests run in order after any
    /// in-flight pass.
    /// </summary>
    void RequestProvision(IReadOnlyCollection<AgentType>? agents = null);

    /// <summary>
    /// Queues a removal pass in the background — un-provisions the managed
    /// entry from every target regardless of the stored URL
    /// (SPEC-20260918-rag-mcp-sync: removal is always an explicit action).
    /// </summary>
    void RequestRemoval(IReadOnlyCollection<AgentType>? agents = null);

    /// <summary>
    /// Runs the provision pass synchronously. When a run is in flight, returns
    /// the current status snapshot instead of duplicating work.
    /// </summary>
    Task<McpProvisionStatus> ProvisionAsync(
        IReadOnlyCollection<AgentType>? agents = null,
        bool forceRemove = false,
        CancellationToken cancellationToken = default);
}
