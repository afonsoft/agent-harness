using System.Text.Json;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// A built-in chat tool exposed to the model via OpenAI function calling
/// (SPEC-20260929-ai-code-provider-chat RF-006). Implementations are
/// auto-confined: the security gateway, path jail and secret scrubbing apply
/// inside <see cref="ExecuteAsync"/> — never per-call approval.
/// </summary>
public interface IChatTool
{
    string Name { get; }
    string Description { get; }
    /// <summary>JSON schema for the <c>parameters</c> field of the OpenAI tool definition.</summary>
    string ParametersJson { get; }

    /// <summary>
    /// Stable capability id used by the toggle list
    /// (SPEC-20261001-chat-capability-registry). Defaults to
    /// <c>tool:{Name}</c>; adapters override (e.g. <c>mcp:{server}/{tool}</c>,
    /// <c>agent:run</c>).
    /// </summary>
    string CapabilityId => $"tool:{Name}";

    /// <summary>Capability kind — drives the master switch that gates this tool.</summary>
    ChatCapabilityKind Kind => ChatCapabilityKind.BuiltinTool;

    /// <summary>Mutating/executing tools surface a confirmation hint in Settings.</summary>
    bool RequiresConfirmation => false;

    /// <summary>Origin label (e.g. MCP server name); null for builtins.</summary>
    string? Origin => null;

    Task<ChatToolResult> ExecuteAsync(JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken);
}

public sealed record ChatToolResult(string Json, bool Refused = false, string? RefusalReason = null);

/// <summary>
/// Live progress reporter handed to tools (SPEC-20261001-chat-agent-delegation
/// FR-001/005, SPEC-20261001-chat-ux-compact FR-003). Implementations are
/// fire-and-forget: <see cref="Report"/> must never throw or block the tool.
/// </summary>
public interface IChatActivityReporter
{
    /// <summary>Reports the current execution phase for the chat activity indicator.</summary>
    /// <param name="phase">e.g. <c>running_tool|running_mcp|running_agent|running_subagent|waiting_permission</c>.</param>
    /// <param name="label">Human label — tool name, <c>server/tool</c>, agent name.</param>
    void Report(string phase, string label);
}

/// <summary>Per-execution context handed to every tool.</summary>
public sealed record ChatToolContext(
    string WorkspacePath,
    Guid ProviderId,
    string ProviderBaseUrl,
    string ProviderApiKey,
    string ImageModel,
    string SearchBackend,
    string SearchUrl,
    string SearchApiKey,
    /// <summary>Owning provider-chat conversation — correlates delegated runs.</summary>
    string? ConversationId = null,
    /// <summary>Model of the current conversation — inherited by sub-agents.</summary>
    string? Model = null,
    /// <summary>Sub-agent nesting depth; sub-agents never receive recursive tools.</summary>
    int DelegationDepth = 0,
    /// <summary>Live status reporter; <see cref="NullChatActivityReporter"/> when absent.</summary>
    IChatActivityReporter? Activity = null,
    /// <summary>
    /// The effective (already filtered) tool set of the current turn — lets
    /// meta-tools like <c>task</c> sub-agents inherit only enabled tools.
    /// </summary>
    IReadOnlyDictionary<string, IChatTool>? ToolSet = null);

/// <summary>Web search backend behind the <c>web_search</c> tool (RF-007).</summary>
public interface ISearchBackend
{
    Task<IReadOnlyList<ChatSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}

public sealed record ChatSearchResult(string Title, string Url, string Snippet);
