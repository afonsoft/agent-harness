using System.Text.Json;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261020-provider-quota-usage RF-009: plan-limit budgets per
/// (provider, windowKind) — conservative defaults, user-saved JSON
/// overrides, and <c>Taskboard:PlanLimits</c> config merge. Pure logic
/// shared by the estimator collector and the service.
/// </summary>
public static class ProviderPlanLimits
{
    /// <summary>Conservative token budgets — "provider:kind" → tokens.</summary>
    public static readonly IReadOnlyDictionary<string, long> Defaults =
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["claude:fiveHour"] = 700_000,
            ["claude:weekly"] = 7_000_000,
            ["codex:fiveHour"] = 300_000,
            ["codex:weekly"] = 4_000_000,
        };

    /// <summary>Tolerant parse of a saved `{"provider":{"kind":n}}` map —
    /// null/garbage/negative values are dropped.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>>
        ParseJson(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return new Dictionary<string, IReadOnlyDictionary<string, long>>();
        }

        try
        {
            var raw = JsonSerializer
                .Deserialize<Dictionary<string, Dictionary<string, long>>>(json);
            return raw is null
                ? new Dictionary<string, IReadOnlyDictionary<string, long>>()
                : raw
                    .Where(p => p.Value is not null)
                    .ToDictionary(
                        p => p.Key,
                        p => (IReadOnlyDictionary<string, long>)p.Value
                            .Where(kv => kv.Value > 0)
                            .ToDictionary(kv => kv.Key, kv => kv.Value,
                                StringComparer.Ordinal),
                        StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, IReadOnlyDictionary<string, long>>();
        }
    }

    /// <summary>Serialize the saved map for persistence.</summary>
    public static string ToJson(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> budgets) =>
        JsonSerializer.Serialize(budgets);

    /// <summary>
    /// Effective budgets for one provider — saved prefs override config
    /// section <paramref name="configured"/> (provider → kind → tokens),
    /// which overrides <see cref="Defaults"/>. Returns "kind → budget".
    /// </summary>
    public static IReadOnlyDictionary<string, long> EffectiveFor(
        string provider,
        IReadOnlyDictionary<string, long>? configured,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>>? saved)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (key, budget) in Defaults)
        {
            if (key.StartsWith($"{provider}:", StringComparison.Ordinal))
            {
                result[key[(provider.Length + 1)..]] = budget;
            }
        }

        if (configured is not null)
        {
            foreach (var (key, budget) in configured)
            {
                if (key.StartsWith($"{provider}:", StringComparison.Ordinal)
                    && budget > 0)
                {
                    result[key[(provider.Length + 1)..]] = budget;
                }
            }
        }

        if (saved is not null
            && saved.TryGetValue(provider, out var userLimits))
        {
            foreach (var (kind, budget) in userLimits)
            {
                if (budget > 0)
                {
                    result[kind] = budget;
                }
            }
        }

        return result;
    }
}
