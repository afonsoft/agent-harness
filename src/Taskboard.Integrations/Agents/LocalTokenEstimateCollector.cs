using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.CliMetrics;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage RF-003/P2+P3: local_estimate fallback —
/// sums <c>CliSessionMetric</c> tokens for the provider's CLI over each
/// configured window and divides by a plan budget. Effective budgets come
/// from <see cref="ProviderPlanLimits.EffectiveFor"/>: saved user prefs →
/// <c>Taskboard:PlanLimits</c> → conservative defaults. Results are
/// confidence=estimated; the service never lets them overwrite a fresh
/// measured row.
/// </summary>
public sealed class LocalTokenEstimateCollector : IProviderUsageCollector
{
    /// <summary>WindowKind → lookback span used for the token sum.</summary>
    private static readonly IReadOnlyDictionary<string, TimeSpan> Spans =
        new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
        {
            [ProviderWindowKinds.FiveHour] = TimeSpan.FromHours(5),
            [ProviderWindowKinds.Daily] = TimeSpan.FromDays(1),
            [ProviderWindowKinds.Weekly] = TimeSpan.FromDays(7),
            [ProviderWindowKinds.Monthly] = TimeSpan.FromDays(30),
        };

    private readonly IServiceProvider _services;
    private readonly AgentCliKind _kind;
    private readonly IReadOnlyDictionary<string, long> _configured;

    public LocalTokenEstimateCollector(
        IServiceProvider services, IConfiguration configuration,
        string provider, AgentCliKind kind)
    {
        _services = services;
        Provider = provider;
        _kind = kind;
        _configured = ConfiguredLimits(configuration);
    }

    public string Provider { get; }

    public async Task<IReadOnlyList<ProviderUsageWindowDto>> CollectAsync(
        string userId, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var metrics = scope.ServiceProvider
            .GetRequiredService<ICliMetricsRepository>();
        // Saved user budgets via the singleton service — creates its own
        // scope internally (no recursion: GetPlanLimits never collects).
        var saved = scope.ServiceProvider
            .GetService<IProviderUsageService>() is { } usage
            ? (await usage.GetPlanLimitsAsync(userId, ct).ConfigureAwait(false)).Saved
            : new Dictionary<string, IReadOnlyDictionary<string, long>>();
        var budgets = ProviderPlanLimits.EffectiveFor(
            Provider, _configured, saved);
        if (budgets.Count == 0)
        {
            return [];
        }

        var now = DateTime.UtcNow;
        var windows = new List<ProviderUsageWindowDto>();
        foreach (var (kind, budget) in budgets)
        {
            if (!Spans.TryGetValue(kind, out var span))
            {
                continue;
            }

            var used = await metrics
                .SumTokensAsync(_kind, now - span, ct).ConfigureAwait(false);
            windows.Add(new ProviderUsageWindowDto(
                Provider, kind, used, budget,
                Math.Clamp(used * 100.0 / budget, 0, 100),
                ResetsAtUtc: null, now,
                ProviderUsageSources.LocalEstimate,
                ProviderUsageConfidences.Estimated));
        }

        return windows;
    }

    /// <summary>
    /// <c>Taskboard:PlanLimits:&lt;provider&gt;:&lt;windowKind&gt;</c> → flat
    /// "provider:kind" map for every configured provider.
    /// </summary>
    internal static IReadOnlyDictionary<string, long> ConfiguredLimits(
        IConfiguration configuration)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
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
}
