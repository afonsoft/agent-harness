namespace Taskboard.Domain.Shared.Configuration;

/// <summary>
/// Resolves <c>HARNESS_*</c> environment variables with one-cycle fallback to
/// the legacy <c>TASKBOARD_*</c> names (SPEC-20260922-harness-home-rename).
/// </summary>
public static class HarnessEnv
{
    public const string Prefix = "HARNESS_";

    public const string LegacyPrefix = "TASKBOARD_";

    /// <summary>
    /// Returns the trimmed value of the <c>HARNESS_*</c> variable, falling back
    /// to the legacy <c>TASKBOARD_*</c> name when unset or empty.
    /// </summary>
    /// <param name="name">Canonical name, e.g. <c>HARNESS_PORT</c>.</param>
    /// <param name="onLegacyFallback">
    /// Optional callback invoked once per call when the legacy name supplied
    /// the value — call sites with a logger should surface a deprecation
    /// warning.
    /// </param>
    public static string? Get(string name, Action<string>? onLegacyFallback = null)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(value))
        {
            return value.Trim();
        }

        var legacy = LegacyPrefix + name[Prefix.Length..];
        var legacyValue = Environment.GetEnvironmentVariable(legacy);
        if (!string.IsNullOrEmpty(legacyValue))
        {
            onLegacyFallback?.Invoke(legacy);
            return legacyValue.Trim();
        }

        return null;
    }

    /// <summary>True when the canonical or legacy name is set and non-empty.</summary>
    public static bool IsSet(string name) => Get(name) is not null;
}
