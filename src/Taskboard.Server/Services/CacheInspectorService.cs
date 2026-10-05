using System.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using StackExchange.Redis;
using Taskboard.Application.Contracts.Configuration;

namespace Taskboard.Server.Services;

/// <summary>
/// Cache inspection for the Settings → Configuration tab (parity with the
/// KnowledgeRAG cache panel): effective provider (<c>memory</c> = HybridCache
/// L1 only, <c>redis</c> = L1+L2), connectivity judged by PING alone, a
/// bounded SCAN for server-side keys, and the registry-tracked key list —
/// the only enumerable view of the in-memory L1.
/// </summary>
public sealed class CacheInspectorService(
    IConfiguration configuration,
    CacheKeyRegistry registry,
    ILogger<CacheInspectorService> logger,
    IConnectionMultiplexer? redis = null)
{
    private const int ScanMaxKeys = 500;
    private const int ScanPageSize = 200;
    private static readonly TimeSpan ScanDeadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RedisPingTimeout = TimeSpan.FromSeconds(2);

    public async Task<CacheStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var conn = configuration["Taskboard:Cache:Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(conn))
        {
            conn = configuration["Taskboard:Cache:Redis"];
        }

        var redisConfigured = !string.IsNullOrWhiteSpace(conn);
        var provider = redisConfigured ? "redis" : "memory";
        var instanceName = configuration["Taskboard:Cache:Redis:InstanceName"];
        if (redisConfigured && string.IsNullOrWhiteSpace(instanceName))
        {
            instanceName = "harness:";
        }

        var defaultExpiration = ParseDuration(
            configuration["Taskboard:Cache:DefaultExpiration"], TimeSpan.FromMinutes(5));
        var localExpiration = ParseDuration(
            configuration["Taskboard:Cache:LocalCacheExpiration"], TimeSpan.FromMinutes(1));
        if (localExpiration > defaultExpiration)
        {
            localExpiration = defaultExpiration;
        }

        var keys = registry.Snapshot()
            .Select(k => new CacheKeyItemDto(k.Key, k.Tags, k.FirstSeenUtc, "registry"))
            .ToDictionary(k => k.Key, StringComparer.Ordinal);

        var redisConnected = false;
        long? serverKeys = null;
        var serverKeysPartial = false;
        string? statsError = null;

        if (redisConfigured && redis is not null)
        {
            (redisConnected, serverKeys, serverKeysPartial, statsError) =
                await ProbeRedisAsync(redis, instanceName ?? string.Empty, keys, cancellationToken)
                    .ConfigureAwait(false);
        }

        return new CacheStatsDto(
            provider,
            redisConfigured,
            instanceName,
            redisConnected,
            serverKeys,
            serverKeysPartial,
            statsError,
            (int)defaultExpiration.TotalSeconds,
            (int)localExpiration.TotalSeconds,
            keys.Values.OrderBy(k => k.Key, StringComparer.Ordinal).ToList());
    }

    private async Task<(bool Connected, long? ServerKeys, bool Partial, string? Error)> ProbeRedisAsync(
        IConnectionMultiplexer multiplexer,
        string instanceName,
        Dictionary<string, CacheKeyItemDto> keys,
        CancellationToken cancellationToken)
    {
        // Connectivity is judged by PING alone — SCAN failures degrade the
        // key list, never flip the badge to disconnected.
        bool connected;
        try
        {
            var ping = multiplexer.GetDatabase().PingAsync();
            var completed = await Task.WhenAny(ping, Task.Delay(RedisPingTimeout, cancellationToken))
                .ConfigureAwait(false);
            connected = completed == ping && !ping.IsFaulted;
            if (ping.IsFaulted)
            {
                logger.LogWarning(ping.Exception?.GetBaseException(), "redis ping failed");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "redis ping failed");
            return (false, null, false, null);
        }

        if (!connected)
        {
            return (false, null, false, null);
        }

        try
        {
            var server = multiplexer.GetEndPoints()
                .Select(ep => multiplexer.GetServer(ep))
                .FirstOrDefault(s => s.IsConnected);
            if (server is null)
            {
                return (true, null, false, "no connected server endpoint");
            }

            // Bounded SCAN (never KEYS *) — cap both page size, total count
            // and wall clock so a stalled scan can't pin the endpoint.
            var count = 0L;
            var partial = false;
            var deadline = Stopwatch.StartNew();
            var pattern = string.IsNullOrEmpty(instanceName) ? "*" : instanceName + "*";
            foreach (var key in server.Keys(pattern: pattern, pageSize: ScanPageSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = key.ToString();
                if (!keys.ContainsKey(name))
                {
                    keys[name] = new CacheKeyItemDto(name, null, DateTimeOffset.MinValue, "redis");
                }

                if (++count >= ScanMaxKeys || deadline.Elapsed >= ScanDeadline)
                {
                    partial = true;
                    break;
                }
            }

            return (true, count, partial, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "redis stats scan failed — connectivity ok, keys degraded");
            return (true, null, false, TrimError(ex));
        }
    }

    private static TimeSpan ParseDuration(string? value, TimeSpan fallback) =>
        TimeSpan.TryParse(value, out var parsed) && parsed > TimeSpan.Zero ? parsed : fallback;

    private static string TrimError(Exception ex)
    {
        var b = ex.GetBaseException();
        var m = $"{b.GetType().Name}: {b.Message}".Replace('\n', ' ').Replace('\r', ' ');
        return m.Length > 200 ? m[..200] : m;
    }
}
