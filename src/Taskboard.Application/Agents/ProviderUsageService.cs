using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage: collect → upsert → warn pipeline.
/// Singleton — scoped collaborators (repo, feed) are resolved per operation.
/// RF-007 warn hysteresis lives on the row (<c>WarnLevel</c>): ≥80 emits
/// quota_warning, ≥100 quota_exhausted (Needs You), re-arms below 75%.
/// </summary>
public sealed class ProviderUsageService : IProviderUsageService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);
    internal const double WarnPct = 80;
    internal const double ExhaustedPct = 100;
    internal const double RearmBelowPct = 75;

    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<IProviderUsageCollector> _collectors;
    private readonly ILogger<ProviderUsageService> _logger;

    public ProviderUsageService(
        IServiceProvider services,
        IEnumerable<IProviderUsageCollector> collectors,
        ILogger<ProviderUsageService> logger)
    {
        _services = services;
        _collectors = collectors.ToList();
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> CollectOnceAsync(
        string userId, string? provider = null, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProviderUsageRepository>();
        var feed = scope.ServiceProvider.GetService<IAgentFeedService>();

        var upserted = 0;
        foreach (var collector in _collectors)
        {
            if (provider is not null
                && !collector.Provider.Equals(provider, StringComparison.Ordinal))
            {
                continue;
            }

            IReadOnlyList<ProviderUsageWindowDto> samples;
            try
            {
                samples = await collector.CollectAsync(userId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Provider offline/broken — the row ages into "stale", never throws.
                _logger.LogWarning(ex,
                    "Usage collector {Provider} failed.", collector.Provider);
                continue;
            }

            foreach (var sample in samples)
            {
                if (sample.WindowKind == ProviderWindowKinds.Na
                    || sample.Used is -1)
                {
                    continue; // RF-010: n/a windows never persist.
                }

                var prior = await repo.GetAsync(
                    sample.Provider, sample.WindowKind, ct).ConfigureAwait(false);
                // RF-003 source precedence: a lower-priority sample never
                // overwrites a fresh higher-priority row — api > cli >
                // local_estimate. It only fills gaps or replaces stale data.
                if (prior is { } p
                    && SourcePriority(p.Source) > SourcePriority(sample.Source)
                    && p.SampledAtUtc >= DateTime.UtcNow - StaleAfter)
                {
                    continue;
                }

                var stored = await repo.UpsertAsync(
                    Sanitize(sample), ct).ConfigureAwait(false);
                upserted++;

                await ApplyWarnTransitionAsync(
                    feed, userId, stored, prior?.WarnLevel ?? 0, repo, ct)
                    .ConfigureAwait(false);
            }
        }

        return upserted;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderUsageRosterEntryDto>> GetRosterAsync(
        CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProviderUsageRepository>();
        var all = await repo.ListAsync(ct: ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        return all
            .GroupBy(w => w.Provider, StringComparer.Ordinal)
            .Select(g => new ProviderUsageRosterEntryDto(
                g.Key,
                [.. g],
                g.Max(w => w.SampledAtUtc),
                g.Max(w => w.SampledAtUtc) < now - StaleAfter))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<ProviderUsageRosterEntryDto?> GetProviderAsync(
        string provider, CancellationToken ct = default) =>
        (await GetRosterAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(r => r.Provider == provider);

    /// <inheritdoc/>
    public async Task<PlanLimitsViewDto> GetPlanLimitsAsync(
        string userId, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var users = scope.ServiceProvider
            .GetRequiredService<IRepository<UserPreference>>();
        var user = (await users.ListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        var saved = ProviderPlanLimits.ParseJson(user?.PlanLimitsJson);

        var providers = saved.Keys
            .Concat(ProviderPlanLimits.Defaults.Keys
                .Select(k => k.Split(':')[0]))
            .Concat(ConfiguredLimits().Keys.Select(k => k.Split(':')[0]))
            .Distinct(StringComparer.Ordinal);
        var effective = new Dictionary<string, IReadOnlyDictionary<string, long>>(
            StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            var merged = ProviderPlanLimits.EffectiveFor(
                provider, ConfiguredLimits(), saved);
            if (merged.Count > 0)
            {
                effective[provider] = merged;
            }
        }

        return new PlanLimitsViewDto(saved, effective);
    }

    /// <inheritdoc/>
    public async Task<PlanLimitsViewDto> SavePlanLimitsAsync(
            string userId,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> budgets,
            CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var users = scope.ServiceProvider
            .GetRequiredService<IRepository<UserPreference>>();
        var user = (await users.ListAsync(ct).ConfigureAwait(false)).FirstOrDefault();

        var clean = budgets
            .Where(p => p.Value is { Count: > 0 })
            .ToDictionary(
                p => p.Key,
                p => (IReadOnlyDictionary<string, long>)p.Value
                    .Where(kv => kv.Value > 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);

        if (user is null)
        {
            await users.AddAsync(new UserPreference(Guid.Empty)
            {
                PlanLimitsJson = ProviderPlanLimits.ToJson(clean),
            }, ct).ConfigureAwait(false);
        }
        else
        {
            user.PlanLimitsJson = ProviderPlanLimits.ToJson(clean);
            await users.UpdateAsync(user, ct).ConfigureAwait(false);
        }

        await users.SaveChangesAsync(ct).ConfigureAwait(false);

        return await GetPlanLimitsAsync(userId, ct).ConfigureAwait(false);
    }

    /// <summary>RF-003 chain order — higher source priority wins on fresh rows.</summary>
    internal static int SourcePriority(string source) => source switch
    {
        ProviderUsageSources.Api => 3,
        ProviderUsageSources.Cli => 2,
        ProviderUsageSources.LocalEstimate => 1,
        _ => 0,
    };

    /// <summary>Taskboard:PlanLimits flat "provider:kind" map (root config).</summary>
    private IReadOnlyDictionary<string, long> ConfiguredLimits()
    {
        var configuration = _services.GetService<IConfiguration>();
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (configuration is null)
        {
            return result;
        }

        foreach (var providerSection in configuration
                     .GetSection("Taskboard:PlanLimits").GetChildren())
        {
            foreach (var kv in providerSection.GetChildren())
            {
                if (long.TryParse(kv.Value, out var budget) && budget > 0)
                {
                    result[$"{providerSection.Key}:{kv.Key}"] = budget;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// RF-007 hysteresis: ≥100 → exhausted once (WarnLevel 2); ≥80 → warning
    /// once (WarnLevel 1); &lt;75 re-arms to 0. WarnLevel is persisted so the
    /// edge survives restarts.
    /// </summary>
    private async Task ApplyWarnTransitionAsync(
        IAgentFeedService? feed, string userId,
        ProviderUsageWindowDto stored, int priorLevel,
        IProviderUsageRepository repo, CancellationToken ct)
    {
        var pct = stored.UsedPct ?? 0;
        if (pct >= ExhaustedPct && priorLevel < 2)
        {
            await repo.SetWarnLevelAsync(
                stored.Provider, stored.WindowKind, 2, ct).ConfigureAwait(false);
            await ProduceAsync(feed, userId, AgentFeedKind.QuotaExhausted,
                $"{stored.Provider} {stored.WindowKind} quota exhausted" +
                " — switch to another CLI",
                stored, needsAttention: true, ct).ConfigureAwait(false);
        }
        else if (pct >= WarnPct && priorLevel < 1)
        {
            await repo.SetWarnLevelAsync(
                stored.Provider, stored.WindowKind, 1, ct).ConfigureAwait(false);
            await ProduceAsync(feed, userId, AgentFeedKind.QuotaWarning,
                $"{stored.Provider} {stored.WindowKind} quota at {pct:F0}%",
                stored, needsAttention: false, ct).ConfigureAwait(false);
        }
        else if (pct < RearmBelowPct && priorLevel > 0)
        {
            await repo.SetWarnLevelAsync(
                stored.Provider, stored.WindowKind, 0, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Feed produce is advisory — a feed failure never fails the tick.</summary>
    private async Task ProduceAsync(
        IAgentFeedService? feed, string userId, AgentFeedKind kind,
        string title, ProviderUsageWindowDto window,
        bool needsAttention, CancellationToken ct)
    {
        if (feed is null)
        {
            return;
        }

        try
        {
            await feed.ProduceAsync(new FeedItemRequest(
                kind, title, $"resets {window.ResetsAtUtc:u}",
                "/agents?tab=sessions", SessionKey: null, window.Provider,
                IssueNumber: null, needsAttention, userId), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Quota feed item for {Provider}/{Kind} failed.",
                window.Provider, window.WindowKind);
        }
    }

    /// <summary>Raw payload ≤2KB with auth-looking keys stripped (NFR).</summary>
    internal static ProviderUsageWindowDto Sanitize(ProviderUsageWindowDto sample)
    {
        if (sample.RawJson is not { Length: > 0 } raw)
        {
            return sample;
        }

        var sanitized = raw
            .Replace("x-api-key", "x-key", StringComparison.OrdinalIgnoreCase)
            .Replace("authorization", "auth", StringComparison.OrdinalIgnoreCase);
        return sample with { RawJson = sanitized.Length > 2048 ? sanitized[..2048] : sanitized };
    }
}
