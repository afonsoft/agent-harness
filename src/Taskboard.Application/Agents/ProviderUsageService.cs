using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

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
    public async Task<int> CollectOnceAsync(string userId, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProviderUsageRepository>();
        var feed = scope.ServiceProvider.GetService<IAgentFeedService>();

        var upserted = 0;
        foreach (var collector in _collectors)
        {
            IReadOnlyList<ProviderUsageWindowDto> samples;
            try
            {
                samples = await collector.CollectAsync(ct).ConfigureAwait(false);
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
                // RF-003 fallback chain: an estimate never overwrites a fresh
                // measured row — it only fills gaps or replaces stale data.
                if (sample.Confidence == ProviderUsageConfidences.Estimated
                    && prior is { Confidence: ProviderUsageConfidences.Measured } p
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
