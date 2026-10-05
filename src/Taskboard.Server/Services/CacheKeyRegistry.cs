using System.Collections.Concurrent;

namespace Taskboard.Server.Services;

/// <summary>One cache key observed by <see cref="TrackingHybridCache"/>.</summary>
public sealed record TrackedCacheKey(string Key, string? Tags, DateTimeOffset FirstSeenUtc);

/// <summary>
/// In-process set of keys written through HybridCache since boot — the only
/// enumerable view of the in-memory L1 (HybridCache exposes no key listing).
/// Populated by <see cref="TrackingHybridCache"/>, read by
/// <see cref="CacheInspectorService"/>.
/// </summary>
public sealed class CacheKeyRegistry
{
    private readonly ConcurrentDictionary<string, TrackedCacheKey> _keys = new(StringComparer.Ordinal);

    public void Track(string key, IEnumerable<string>? tags)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var tagList = tags is null ? null : string.Join(',', tags);
        _keys[key] = new TrackedCacheKey(key, tagList, DateTimeOffset.UtcNow);
    }

    public void Untrack(string key) => _keys.TryRemove(key, out _);

    public void UntrackTag(string tag)
    {
        foreach (var kvp in _keys)
        {
            if (kvp.Value.Tags is { } tagList
                && tagList.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(tag, StringComparer.Ordinal))
            {
                _keys.TryRemove(kvp.Key, out _);
            }
        }
    }

    public IReadOnlyList<TrackedCacheKey> Snapshot() =>
        _keys.Values.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();

    public int Count => _keys.Count;
}
