using Microsoft.Extensions.Caching.Hybrid;
using Taskboard.Dtos;

namespace Taskboard.Server.Services;

/// <summary>
/// Drift report cache over <see cref="HybridCache"/> — L1 in-process plus L2
/// Redis when configured (SPEC-20261004-redis-hybrid-cache RF-005). Lets
/// <c>/api/specs/drift-report</c> answer without rescanning ~100 spec files
/// per request, and survives a process restart when L2 is active.
/// </summary>
public sealed class SpecDriftReportCache
{
    /// <summary>Logical-invalidation tag for every drift-report entry.</summary>
    public const string DriftTag = "spec-drift";

    private const string Key = "specs:drift-report";
    private static readonly string[] Tags = [DriftTag];
    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        // L2 outlives the process (several hourly-scan ticks); L1 turns over
        // fast enough that a SetAsync refresh is visible quickly.
        Expiration = TimeSpan.FromHours(6),
        LocalCacheExpiration = TimeSpan.FromMinutes(5),
    };

    private readonly HybridCache _cache;

    public SpecDriftReportCache(HybridCache cache) => _cache = cache;

    /// <summary>Cached report or a single-flight call to <paramref name="factory"/>.</summary>
    public ValueTask<SpecDriftReportDto> GetOrCreateAsync(
        Func<CancellationToken, ValueTask<SpecDriftReportDto>> factory,
        CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(Key, factory, EntryOptions, Tags, cancellationToken);

    /// <summary>Push the freshest report (hourly scan) into L1+L2.</summary>
    public ValueTask SetAsync(SpecDriftReportDto report, CancellationToken cancellationToken) =>
        _cache.SetAsync(Key, report, EntryOptions, Tags, cancellationToken);
}
