using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.CliMetrics;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage RF-003/P2: local_estimate fallback —
/// sums <c>CliSessionMetric</c> tokens for the provider's CLI over each
/// configured window and divides by a plan budget
/// (<c>Taskboard:PlanLimits</c>, keys "provider:windowKind" → token limit).
/// Unconfigured providers fall back to <see cref="Defaults"/> — results are
/// confidence=estimated, never measured. The service skips an estimate when
/// a fresher measured row already exists.
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

    /// <summary>Conservative token budgets per (provider, windowKind) when
    /// Taskboard:PlanLimits doesn't configure the provider.</summary>
    private static readonly IReadOnlyDictionary<string, long> Defaults =
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["claude:fiveHour"] = 700_000,
            ["claude:weekly"] = 7_000_000,
            ["codex:fiveHour"] = 300_000,
            ["codex:weekly"] = 4_000_000,
        };

    private readonly IServiceProvider _services;
    private readonly AgentCliKind _kind;
    private readonly IReadOnlyDictionary<string, long> _limits;

    public LocalTokenEstimateCollector(
        IServiceProvider services, IConfiguration configuration,
        string provider, AgentCliKind kind)
    {
        _services = services;
        Provider = provider;
        _kind = kind;
        _limits = LimitsFor(provider, configuration);
    }

    public string Provider { get; }

    public async Task<IReadOnlyList<ProviderUsageWindowDto>> CollectAsync(
        CancellationToken ct = default)
    {
        if (_limits.Count == 0)
        {
            return [];
        }

        await using var scope = _services.CreateAsyncScope();
        var metrics = scope.ServiceProvider
            .GetRequiredService<ICliMetricsRepository>();
        var now = DateTime.UtcNow;
        var windows = new List<ProviderUsageWindowDto>();
        foreach (var (key, budget) in _limits)
        {
            var kind = key[(Provider.Length + 1)..];
            if (!Spans.TryGetValue(kind, out var span) || budget <= 0)
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
    /// Configured limits for the provider (<c>Taskboard:PlanLimits</c>
    /// "provider:kind" keys); falls back to <see cref="Defaults"/> — never
    /// empty for providers listed there.
    /// </summary>
    private static IReadOnlyDictionary<string, long> LimitsFor(
        string provider, IConfiguration configuration)
    {
        // Taskboard:PlanLimits:<provider>:<windowKind> — nested section per
        // provider so env vars (PlanLimits__claude__fiveHour) bind naturally.
        var section = configuration.GetSection("Taskboard:PlanLimits");
        var configured = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var providerSection in section.GetChildren())
        {
            if (!providerSection.Key.Equals(provider, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var kv in providerSection.GetChildren())
            {
                if (long.TryParse(kv.Value, out var budget) && budget > 0)
                {
                    configured[$"{provider}:{kv.Key}"] = budget;
                }
            }
        }

        if (configured.Count > 0)
        {
            return configured;
        }

        return Defaults
            .Where(kv => kv.Key.StartsWith($"{provider}:", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }
}
