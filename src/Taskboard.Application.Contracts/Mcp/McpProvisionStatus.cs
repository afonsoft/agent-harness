namespace Taskboard.Application.Contracts.Mcp;

/// <summary>Lifecycle of an MCP provisioning run (SPEC-20260917-rag-mcp-provisioning).</summary>
public enum McpProvisionState
{
    Idle,
    Running,
    Succeeded,
    Failed
}

/// <summary>
/// Snapshot of the RAG/MCP provisioning engine. The API key is never carried
/// in this payload — only <see cref="ConfiguredUrl"/> and <see cref="ServerName"/>.
/// </summary>
public sealed record McpProvisionStatus(
    McpProvisionState State,
    DateTimeOffset? LastRunUtc,
    long? LastDurationMs,
    string? ServerName,
    string? ConfiguredUrl,
    IReadOnlyList<McpAgentResult> Agents,
    string? Error)
{
    public static readonly McpProvisionStatus Empty =
        new(McpProvisionState.Idle, null, null, null, null, [], null);
}
