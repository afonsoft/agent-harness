using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261025 C3: skills catalog v2 rules —
/// <c>Taskboard:Chat:Skills:Pinned</c> (JSON name list; pinned skills sort
/// first in the prompt catalog and survive the ≤40 cap, ordered by pin
/// position, capped at <see cref="MaxPins"/>) and
/// <c>Taskboard:Chat:Skills:Disabled</c> (JSON kill list — same effect as
/// per-item <c>Capabilities:Disabled</c> entries but environment-wide).
/// </summary>
public static class ChatSkillRules
{
    public const string PinnedKey = "Taskboard:Chat:Skills:Pinned";
    public const string DisabledKey = "Taskboard:Chat:Skills:Disabled";

    /// <summary>Pin budget — protects the prompt against pin-everything configs.</summary>
    public const int MaxPins = 5;

    /// <summary>Ordered pin list (first wins ordering), truncated at <see cref="MaxPins"/>.</summary>
    public static IReadOnlyList<string> Pinned(IConfiguration configuration) =>
        JsonNameList(configuration[PinnedKey]).Take(MaxPins).ToList();

    /// <summary>Environment kill list — skill names blocked regardless of per-item toggles.</summary>
    public static ISet<string> Disabled(IConfiguration configuration) =>
        new HashSet<string>(JsonNameList(configuration[DisabledKey]), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Catalog ordering — pinned first (in pin order), the rest alphabetical.
    /// Apply before the prompt cap so pins always survive truncation.
    /// </summary>
    public static IEnumerable<T> OrderCatalog<T>(
        IEnumerable<T> skills, IConfiguration configuration, Func<T, string> name)
    {
        var pins = Pinned(configuration);
        var rank = pins
            .Select((n, i) => (n, i))
            .ToDictionary(x => x.n, x => x.i, StringComparer.OrdinalIgnoreCase);
        return skills
            .OrderBy(s => rank.TryGetValue(name(s), out var i) ? i : int.MaxValue)
            .ThenBy(name, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> JsonNameList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
