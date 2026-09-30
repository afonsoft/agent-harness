using System.Text.Json;

using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Mcp;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Exposes one remote MCP tool as an <see cref="IChatTool"/>
/// (SPEC-20261001-chat-mcp-client FR-003): name <c>mcp_{server}_{tool}</c>,
/// capability id <c>mcp:{server}/{tool}</c>, description tagged with the origin
/// server, dispatch via <see cref="IMcpClientManager.CallAsync"/>.
/// </summary>
public sealed class McpToolAdapter(
    IMcpClientManager manager,
    string serverName,
    string remoteToolName,
    string exposedName,
    string remoteDescription,
    string parametersJson) : IChatTool
{
    public string Name { get; } = exposedName;
    public string Description { get; } = $"[mcp:{serverName}] {remoteDescription}";
    public string ParametersJson { get; } = parametersJson;
    public string CapabilityId { get; } = $"mcp:{serverName}/{remoteToolName}";
    public ChatCapabilityKind Kind => ChatCapabilityKind.McpTool;
    public string? Origin => serverName;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
        manager.CallAsync(serverName, remoteToolName, arguments, context, cancellationToken);
}
