using Microsoft.Extensions.Caching.Hybrid;

namespace Taskboard.Server.Services;

/// <summary>
/// HybridCache decorator that mirrors every write/removal into the
/// <see cref="CacheKeyRegistry"/> so the Settings → Configuration tab can
/// list the keys currently held by the cache (HybridCache itself exposes no
/// enumeration). Registered once in DI — call sites keep using
/// <see cref="HybridCache"/> unchanged.
/// </summary>
public sealed class TrackingHybridCache(HybridCache inner, CacheKeyRegistry registry) : HybridCache
{
    public override async ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.GetOrCreateAsync(key, state, factory, options, tags, cancellationToken)
            .ConfigureAwait(false);
        // A hit means the entry exists; a miss just created it — either way
        // the key now holds a value worth listing.
        registry.Track(key, tags);
        return result;
    }

    public override async ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        await inner.SetAsync(key, value, options, tags, cancellationToken).ConfigureAwait(false);
        registry.Track(key, tags);
    }

    public override async ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await inner.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        registry.Untrack(key);
    }

    public override async ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var materialized = keys as string[] ?? keys.ToArray();
        await inner.RemoveAsync(materialized, cancellationToken).ConfigureAwait(false);
        foreach (var key in materialized)
        {
            registry.Untrack(key);
        }
    }

    public override async ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        await inner.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        registry.UntrackTag(tag);
    }

    public override async ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        var materialized = tags as string[] ?? tags.ToArray();
        await inner.RemoveByTagAsync(materialized, cancellationToken).ConfigureAwait(false);
        foreach (var tag in materialized)
        {
            registry.UntrackTag(tag);
        }
    }
}
