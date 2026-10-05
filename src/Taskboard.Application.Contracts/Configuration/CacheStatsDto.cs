namespace Taskboard.Application.Contracts.Configuration;

/// <summary>
/// A single cached key known to the server. <paramref name="Source"/> is
/// <c>registry</c> when the key was written through HybridCache since boot
/// (the only enumerable view of the in-memory L1) or <c>redis</c> when it was
/// found by a bounded SCAN on the shared L2 instance.
/// </summary>
public sealed record CacheKeyItemDto(
    string Key,
    string? Tags,
    DateTimeOffset FirstSeenUtc,
    string Source);

/// <summary>
/// Cache inspection snapshot for the Settings → Configuration tab:
/// effective provider (in-memory L1 vs Redis L1+L2), connectivity, effective
/// TTLs and the list of keys currently known to hold a value.
/// </summary>
/// <param name="Provider"><c>memory</c> or <c>redis</c>.</param>
/// <param name="RedisConfigured">Whether a Redis connection string is set.</param>
/// <param name="InstanceName">Effective key prefix on Redis (default <c>harness:</c>).</param>
/// <param name="RedisConnected">PING answered within the bound — Redis only.</param>
/// <param name="ServerKeys">Keys under the instance prefix on the server (bounded SCAN).</param>
/// <param name="ServerKeysPartial">SCAN hit the cap/deadline — count is a lower bound.</param>
/// <param name="StatsError">Bounded digest when server stats failed but the endpoint answered.</param>
/// <param name="DefaultExpirationSeconds">Effective L2 TTL (<c>Taskboard:Cache:DefaultExpiration</c>).</param>
/// <param name="LocalCacheExpirationSeconds">Effective L1 TTL (clamped to ≤ default).</param>
/// <param name="Keys">Known keys — registry-tracked writes plus Redis SCAN hits.</param>
public sealed record CacheStatsDto(
    string Provider,
    bool RedisConfigured,
    string? InstanceName,
    bool RedisConnected,
    long? ServerKeys,
    bool ServerKeysPartial,
    string? StatsError,
    int DefaultExpirationSeconds,
    int LocalCacheExpirationSeconds,
    IReadOnlyList<CacheKeyItemDto> Keys);
