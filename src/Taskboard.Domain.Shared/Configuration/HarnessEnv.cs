namespace Taskboard.Domain.Shared.Configuration;

/// <summary>
/// Resolves <c>HARNESS_*</c> environment variables — the only env prefix read
/// since SPEC-20260928-taskboard-env-fallback-removal (the one-cycle
/// <c>TASKBOARD_*</c> fallback introduced by SPEC-20260922-harness-home-rename
/// has ended).
/// </summary>
public static class HarnessEnv
{
    public const string Prefix = "HARNESS_";

    /// <summary>Returns the trimmed value of the <c>HARNESS_*</c> variable.</summary>
    /// <param name="name">Canonical name, e.g. <c>HARNESS_PORT</c>.</param>
    public static string? Get(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? null : value.Trim();
    }

    /// <summary>True when the canonical name is set and non-empty.</summary>
    public static bool IsSet(string name) => Get(name) is not null;
}
