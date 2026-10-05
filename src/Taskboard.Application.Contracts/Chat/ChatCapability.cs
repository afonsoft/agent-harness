namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Kind of invocable chat capability (SPEC-20261001-chat-capability-registry).
/// Builtin tools and MCP tools ship as <see cref="IChatTool"/> adapters;
/// skills are invocable via the <c>use_skill</c> tool; agent delegation maps
/// to the <c>run_agent</c>/<c>task</c> tools.
/// </summary>
public enum ChatCapabilityKind
{
    BuiltinTool,
    McpTool,
    Skill,
    AgentDelegation,
}

/// <summary>
/// One invocable capability exposed to the chat model, as listed by the
/// capability registry for Settings → Chat and for tool-set resolution.
/// <paramref name="Id"/> is the stable toggle key: <c>tool:{name}</c>,
/// <c>mcp:{server}/{tool}</c>, <c>skill:{name}</c>, <c>agent:run|task</c>.
/// </summary>
public sealed record ChatCapability(
    string Id,
    ChatCapabilityKind Kind,
    string Name,
    string Description,
    bool Enabled,
    bool RequiresConfirmation,
    string? Origin,
    /// <summary>
    /// SPEC-20261005-chat-tool-approval RF-008: per-tool policy override
    /// (<c>ask|never|allow</c>) or null when the preset decides.
    /// </summary>
    string? ApprovalPolicy = null);
