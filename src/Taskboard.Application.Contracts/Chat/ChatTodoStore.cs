using System.Text.Json;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Per-conversation task list behind the <c>todo</c> chat tool — the OpenCode
/// <c>todowrite</c> feature: the model tracks multi-step work items so progress
/// survives across turns of the same conversation. Items live in
/// <c><dataDir>/chat-todos.json</c> keyed by conversation id, guarded by a lock.
/// </summary>
public sealed class ChatTodoStore(string dataDir)
{
    /// <summary>Hard cap per conversation — keeps the tool result cheap.</summary>
    public const int MaxItemsPerConversation = 50;

    public const int MaxContentChars = 500;

    private readonly object _gate = new();
    private readonly string _file = Path.Combine(dataDir, "chat-todos.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public IReadOnlyList<ChatTodoItem> List(string conversationId)
    {
        lock (_gate)
        {
            return ReadUnsafe().TryGetValue(conversationId, out var items)
                ? items
                : [];
        }
    }

    /// <summary>Replaces the whole list — mirrors todowrite semantics.</summary>
    public void Write(string conversationId, IReadOnlyList<ChatTodoItem> items)
    {
        lock (_gate)
        {
            var all = ReadUnsafe();
            var trimmed = items
                .Take(MaxItemsPerConversation)
                .Select(i => i with
                {
                    Content = i.Content.Length <= MaxContentChars ? i.Content : i.Content[..MaxContentChars],
                })
                .ToList();
            all[conversationId] = trimmed;
            WriteUnsafe(all);
        }
    }

    private Dictionary<string, List<ChatTodoItem>> ReadUnsafe()
    {
        try
        {
            if (!File.Exists(_file))
            {
                return [];
            }

            return JsonSerializer.Deserialize<Dictionary<string, List<ChatTodoItem>>>(
                File.ReadAllText(_file)) ?? [];
        }
        catch (JsonException)
        {
            // Corrupt file must never break the turn — start fresh.
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private void WriteUnsafe(Dictionary<string, List<ChatTodoItem>> all)
    {
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(_file, JsonSerializer.Serialize(all, Json));
    }
}

/// <summary>One tracked work item — <c>status</c> is pending|in_progress|completed.</summary>
public sealed record ChatTodoItem(string Id, string Content, string Status);
