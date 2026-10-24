using System.Text.Json;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// SPEC-20261023-p0-agent-hardening RF-D02: discovery over the deferred
/// catalog — returns matching name/description pairs (never JSON schemas);
/// the model enables one via <c>tool_activate</c>.
/// </summary>
public sealed class ToolSearchTool(IDeferredToolCatalog catalog) : IChatTool
{
    public string Name => "tool_search";
    public string Description =>
        "Search deferred (hidden) tools by keyword and return name/description pairs. "
        + "Use when you need a capability not in your tool list — then call tool_activate to enable it.";
    public string ParametersJson => """
        {"type":"object","properties":{"query":{"type":"string","description":"Keyword to match against tool names and descriptions; omit to list every deferred tool."}}}
        """;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var query = arguments.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
            ? q.GetString()
            : null;
        var matches = catalog.Search(context.ConversationId ?? string.Empty, query);
        return Task.FromResult(new ChatToolResult(JsonSerializer.Serialize(new
        {
            matches = matches.Select(e => new { name = e.Name, description = e.Description, available = e.Available }),
        })));
    }
}

/// <summary>
/// SPEC-20261023-p0-agent-hardening RF-D02: promotes a deferred tool — its
/// schema joins the next provider call (append-only, cache-prefix stable).
/// <c>"*"</c>/<c>"all"</c> activates every available deferred tool — the
/// conversation-level allow-all escape hatch (RF-D03).
/// </summary>
public sealed class ToolActivateTool(IDeferredToolCatalog catalog) : IChatTool
{
    public string Name => "tool_activate";
    public string Description =>
        "Enable a deferred tool for this conversation: its schema is appended to the next model call. "
        + "Pass \"*\" to activate every available deferred tool.";
    public string ParametersJson => """
        {"type":"object","properties":{"name":{"type":"string","description":"Deferred tool name (see tool_search), or \"*\" to activate all available deferred tools."}},"required":["name"]}
        """;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var name = arguments.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { error = "name is required" }), Refused: true, "empty name"));
        }

        var result = catalog.Activate(context.ConversationId ?? string.Empty, name.Trim());
        return Task.FromResult(new ChatToolResult(JsonSerializer.Serialize(result.Ok
            ? (object)new { ok = true, activated = result.Activated }
            : new { ok = false, error = result.Error })));
    }
}
