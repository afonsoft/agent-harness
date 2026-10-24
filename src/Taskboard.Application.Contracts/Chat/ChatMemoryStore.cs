using System.Text.Json;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Persists assistant memories under <c><dataDir>/chat-memory.json</c> — the
/// open-webui "Memory" feature (SPEC-20261001-ai-chat-openwebui): facts the
/// model chooses to remember survive across conversations. The store is a
/// small JSON file guarded by a lock; content is capped and scrubbed by the
/// caller's redactor before it reaches disk.
/// </summary>
public sealed class ChatMemoryStore(string dataDir)
{
    /// <summary>Hard cap — memory prompts stay cheap.</summary>
    public const int MaxItems = 500;

    public const int MaxContentChars = 2000;

    private readonly object _gate = new();
    private readonly string _file = Path.Join(dataDir, "chat-memory.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public IReadOnlyList<ChatMemoryItem> List()
    {
        lock (_gate)
        {
            return ReadUnsafe().OrderByDescending(m => m.UpdatedAt).ToList();
        }
    }

    public IReadOnlyList<ChatMemoryItem> Search(string query, int maxResults)
    {
        var term = query.Trim();
        lock (_gate)
        {
            return ReadUnsafe()
                .Where(m => m.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.UpdatedAt)
                .Take(Math.Clamp(maxResults, 1, 50))
                .ToList();
        }
    }

    public ChatMemoryItem Add(string content, DateTime now)
    {
        var item = new ChatMemoryItem(
            Guid.NewGuid(),
            content.Length <= MaxContentChars ? content : content[..MaxContentChars],
            now,
            now);
        lock (_gate)
        {
            var items = ReadUnsafe();
            items.Add(item);
            if (items.Count > MaxItems)
            {
                items = items.OrderByDescending(m => m.UpdatedAt).Take(MaxItems).ToList();
            }

            WriteUnsafe(items);
        }

        return item;
    }

    public bool Update(Guid id, string content, DateTime now)
    {
        lock (_gate)
        {
            var items = ReadUnsafe();
            var index = items.FindIndex(m => m.Id == id);
            if (index < 0)
            {
                return false;
            }

            items[index] = items[index] with
            {
                Content = content.Length <= MaxContentChars ? content : content[..MaxContentChars],
                UpdatedAt = now,
            };
            WriteUnsafe(items);
            return true;
        }
    }

    public bool Delete(Guid id)
    {
        lock (_gate)
        {
            var items = ReadUnsafe();
            var removed = items.RemoveAll(m => m.Id == id);
            if (removed > 0)
            {
                WriteUnsafe(items);
            }

            return removed > 0;
        }
    }

    /// <summary>
    /// SPEC-20261024-p1-agent-reliability RF-F01: one atomic consolidation
    /// pass under the store lock — expires facts whose <c>UpdatedAt</c> is
    /// older than <paramref name="cutoffUtc"/>, dedupes identical content
    /// keeping the freshest entry, then LRU-caps at <paramref name="maxFacts"/>.
    /// The file is rewritten only when the set actually changed.
    /// </summary>
    public ChatMemoryConsolidationReport Consolidate(DateTime cutoffUtc, int maxFacts)
    {
        lock (_gate)
        {
            var items = ReadUnsafe();
            if (items.Count == 0)
            {
                return new ChatMemoryConsolidationReport(0, 0, 0, 0);
            }

            var expired = items.RemoveAll(m => m.UpdatedAt < cutoffUtc);

            var seen = new Dictionary<string, ChatMemoryItem>(StringComparer.Ordinal);
            var deduped = 0;
            foreach (var item in items.OrderByDescending(m => m.UpdatedAt))
            {
                if (!seen.TryAdd(item.Content, item))
                {
                    deduped++;
                }
            }

            var kept = seen.Values.OrderByDescending(m => m.UpdatedAt).ToList();
            var cap = Math.Max(1, maxFacts);
            var evicted = Math.Max(0, kept.Count - cap);
            if (evicted > 0)
            {
                kept = kept.Take(cap).ToList();
            }

            if (expired > 0 || deduped > 0 || evicted > 0)
            {
                WriteUnsafe(kept);
            }

            return new ChatMemoryConsolidationReport(kept.Count, deduped, expired, evicted);
        }
    }

    private List<ChatMemoryItem> ReadUnsafe()
    {
        try
        {
            if (!File.Exists(_file))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<ChatMemoryItem>>(File.ReadAllText(_file)) ?? [];
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

    private void WriteUnsafe(List<ChatMemoryItem> items)
    {
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(_file, JsonSerializer.Serialize(items, Json));
    }
}

/// <summary>One remembered fact — free-form text chosen by the model.</summary>
public sealed record ChatMemoryItem(Guid Id, string Content, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>RF-F02: outcome of one <see cref="ChatMemoryStore.Consolidate"/> pass.</summary>
public sealed record ChatMemoryConsolidationReport(int Kept, int Deduped, int Expired, int Evicted);
