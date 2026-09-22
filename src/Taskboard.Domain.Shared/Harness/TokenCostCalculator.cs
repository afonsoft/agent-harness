
namespace Taskboard.Harness;

/// <summary>
/// Exact decimal cost calculation from token usage and the price table
/// (SPEC-20260919-ade-observability-finops RF-002):
/// `cost = input*In + output*Out + cacheWrite*Cw + cacheRead*Cr` per 1M tokens.
/// Model matching: exact name → longest prefix → `"*"` fallback.
/// </summary>
public static class TokenCostCalculator
{
    public static decimal Calculate(IReadOnlyList<ModelPriceRateInfo> rates, string? modelName, TokenUsage usage)
    {
        var rate = Resolve(rates, modelName);
        if (rate is null)
        {
            return 0m;
        }

        const decimal perMillion = 1_000_000m;
        return
            usage.InputTokens * rate.InputPer1M / perMillion +
            usage.OutputTokens * rate.OutputPer1M / perMillion +
            usage.CacheWriteTokens * rate.CacheWritePer1M / perMillion +
            usage.CacheReadTokens * rate.CacheReadPer1M / perMillion;
    }

    /// <summary>Longest-prefix match wins; `"*"` is the global fallback.</summary>
    public static ModelPriceRateInfo? Resolve(IReadOnlyList<ModelPriceRateInfo> rates, string? modelName)
    {
        ModelPriceRateInfo? wildcard = null;
        ModelPriceRateInfo? best = null;

        foreach (var rate in rates)
        {
            if (rate.ModelPattern == "*")
            {
                wildcard = rate;
                continue;
            }

            if (modelName is null)
            {
                continue;
            }

            var matches = modelName.Equals(rate.ModelPattern, StringComparison.OrdinalIgnoreCase)
                || modelName.StartsWith(rate.ModelPattern, StringComparison.OrdinalIgnoreCase);
            if (matches && (best is null || rate.ModelPattern.Length > best.ModelPattern.Length))
            {
                best = rate;
            }
        }

        return best ?? wildcard;
    }

    /// <summary>
    /// Like <see cref="Resolve"/> but treats the `"*"` wildcard as no match —
    /// used to detect CLI sessions whose model has no real price coverage so
    /// the flat fallback applies (SPEC-20260922-finops-dashboard-detail RF-007).
    /// </summary>
    public static ModelPriceRateInfo? ResolveSpecific(IReadOnlyList<ModelPriceRateInfo> rates, string? modelName)
    {
        var rate = Resolve(rates, modelName);
        return rate is { ModelPattern: "*" } ? null : rate;
    }
}
