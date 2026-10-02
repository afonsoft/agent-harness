using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Persistent memory — the open-webui "Memory" feature
/// (SPEC-20261001-ai-chat-openwebui). The model saves durable facts about the
/// operator, the host or preferences (<c>action=add</c>) and recalls them in
/// later conversations (<c>list</c>/<c>search</c>); <c>update</c>/<c>delete</c>
/// maintain the store. Entries live in <c><dataDir>/chat-memory.json</c>, are
/// secret-scrubbed and capped.
/// </summary>
public sealed class MemoryTool(ChatMemoryStore store, ISecretRedactor? redactor = null) : IChatTool
{
    public string Name => "memory";
    public string Description =>
        "Remember and recall durable facts across conversations (preferences, host details, project context). "
        + "Actions: add(content), list(limit?), search(query, limit?), update(id, content), delete(id).";
    public string ParametersJson => """
        {"type":"object","properties":{"action":{"type":"string","enum":["add","list","search","update","delete"],"description":"Operation to perform"},"content":{"type":"string","description":"Fact text to store (add/update)"},"query":{"type":"string","description":"Search term (search)"},"id":{"type":"string","description":"Memory id (update/delete)"},"limit":{"type":"integer","description":"Max items (list/search, default 20)"}},"required":["action"]}
        """;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var action = arguments.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String
            ? a.GetString() ?? string.Empty
            : string.Empty;
        var limit = arguments.TryGetProperty("limit", out var l) && l.TryGetInt32(out var lv) ? lv : 20;

        return action switch
        {
            "add" => AddMemory(arguments),
            "list" => ListMemories(limit),
            "search" => SearchMemories(arguments, limit),
            "update" => UpdateMemory(arguments),
            "delete" => DeleteMemory(arguments),
            _ => Task.FromResult(Error($"unknown action '{action}' — use add|list|search|update|delete", refused: true)),
        };
    }

    private Task<ChatToolResult> AddMemory(JsonElement arguments)
    {
        var content = ReadContent(arguments);
        if (content.Length == 0)
        {
            return Task.FromResult(Error("content is required for add", refused: true));
        }

        var item = store.Add(Scrub(content), DateTime.UtcNow);
        return Task.FromResult(Ok(new { saved = true, memory = ToJson(item) }));
    }

    private Task<ChatToolResult> ListMemories(int limit)
    {
        var items = store.List().Take(Math.Clamp(limit, 1, 100)).Select(ToJson).ToList();
        return Task.FromResult(Ok(new { count = items.Count, memories = items }));
    }

    private Task<ChatToolResult> SearchMemories(JsonElement arguments, int limit)
    {
        var query = arguments.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
            ? q.GetString() ?? string.Empty
            : string.Empty;
        if (query.Trim().Length == 0)
        {
            return Task.FromResult(Error("query is required for search", refused: true));
        }

        var items = store.Search(query, limit).Select(ToJson).ToList();
        return Task.FromResult(Ok(new { count = items.Count, memories = items }));
    }

    private Task<ChatToolResult> UpdateMemory(JsonElement arguments)
    {
        if (!TryReadId(arguments, out var id))
        {
            return Task.FromResult(Error("id is required for update", refused: true));
        }

        var content = ReadContent(arguments);
        if (content.Length == 0)
        {
            return Task.FromResult(Error("content is required for update", refused: true));
        }

        return store.Update(id, Scrub(content), DateTime.UtcNow)
            ? Task.FromResult(Ok(new { updated = true, id }))
            : Task.FromResult(Error($"memory '{id}' not found"));
    }

    private Task<ChatToolResult> DeleteMemory(JsonElement arguments)
    {
        if (!TryReadId(arguments, out var id))
        {
            return Task.FromResult(Error("id is required for delete", refused: true));
        }

        return store.Delete(id)
            ? Task.FromResult(Ok(new { deleted = true, id }))
            : Task.FromResult(Error($"memory '{id}' not found"));
    }

    private string Scrub(string content) => redactor?.Redact(content) ?? content;

    private static string ReadContent(JsonElement arguments) =>
        arguments.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? (c.GetString() ?? string.Empty).Trim()
            : string.Empty;

    private static bool TryReadId(JsonElement arguments, out Guid id)
    {
        id = Guid.Empty;
        return arguments.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String
            && Guid.TryParse(i.GetString(), out id);
    }

    private static object ToJson(ChatMemoryItem item) => new
    {
        id = item.Id,
        content = item.Content,
        createdAt = item.CreatedAt,
        updatedAt = item.UpdatedAt,
    };

    private static ChatToolResult Ok(object payload) =>
        new(JsonSerializer.Serialize(payload));

    private static ChatToolResult Error(string message, bool refused = false) =>
        new(JsonSerializer.Serialize(new { error = message }), Refused: refused, refused ? message : null);
}
