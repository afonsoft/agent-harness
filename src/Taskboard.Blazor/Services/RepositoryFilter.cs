using System.Text.RegularExpressions;

namespace Taskboard.Blazor.Services;

/// <summary>
/// SPEC-20260915-wasm-post-migration-hardening RF-002: pure filtering,
/// validation and selection logic for <c>RepositoryCombobox</c>, extracted
/// from the component so it can be unit-tested without a renderer.
/// </summary>
public static class RepositoryFilter
{
    /// <summary>Maximum number of options rendered in the dropdown.</summary>
    public const int MaxResults = 50;

    private static readonly Regex RepositoryNamePattern =
        new(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Case-insensitive substring filter over <c>owner/repo</c> names,
    /// capped at <paramref name="maxResults"/>. Null/empty filter returns the
    /// full (capped) list.
    /// </summary>
    public static IReadOnlyList<string> Filter(
        IReadOnlyList<string> repositories, string? filter, int maxResults = MaxResults) =>
        (string.IsNullOrEmpty(filter)
            ? repositories
            : repositories.Where(r => r.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        .Take(maxResults)
        .ToList();

    /// <summary>True when the value is a well-formed <c>owner/repo</c> name.</summary>
    public static bool IsValidRepositoryName(string? value) =>
        value is not null && RepositoryNamePattern.IsMatch(value.Trim());

    /// <summary>Trims a committed value (null becomes empty).</summary>
    public static string NormalizeCommit(string? value) => (value ?? string.Empty).Trim();

    /// <summary>
    /// Wrap-around keyboard navigation. <paramref name="delta"/> is +1 for
    /// ArrowDown (from -1 lands on 0) and -1 for ArrowUp (from 0 or -1 lands
    /// on the last option). Returns -1 when there are no options.
    /// </summary>
    public static int MoveActiveIndex(int currentIndex, int count, int delta)
    {
        if (count <= 0)
        {
            return -1;
        }

        return delta >= 0
            ? (currentIndex + delta) % count
            : currentIndex <= 0 ? count - 1 : currentIndex + delta;
    }

    /// <summary>
    /// Clamps a selection index to <c>[0, count)</c>; returns -1 when the
    /// index is out of range or the list is empty.
    /// </summary>
    public static int ClampActiveIndex(int index, int count) =>
        index >= 0 && index < count ? index : -1;
}
