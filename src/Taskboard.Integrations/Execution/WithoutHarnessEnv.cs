namespace Taskboard.Integrations.Execution;

public static class WithoutHarnessEnv
{
    // TASKBOARD_* stays scrubbed even after the read fallback ended
    // (SPEC-20260928-taskboard-env-fallback-removal RF-003): a legacy env must
    // never leak into agent processes.
    public static void RemoveFrom(IDictionary<string, string?> environment)
    {
        var keys = environment.Keys
            .Where(k => k.StartsWith("HARNESS_", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("TASKBOARD_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in keys)
        {
            environment.Remove(key);
        }
    }

    /// <summary>
    /// Full scrub for spawned agent processes: harness vars plus the inherited
    /// <c>PWD</c>/<c>OLDPWD</c>. A stale <c>PWD</c> defeats worktree isolation —
    /// CLIs like opencode resolve their project directory from it instead of the
    /// process working directory (found by E2E: writes landed in the server's
    /// launch dir while the worktree stayed empty).
    /// </summary>
    public static void Apply(IDictionary<string, string?> environment, string? workingDirectory)
    {
        RemoveFrom(environment);
        environment.Remove("OLDPWD");
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            environment.Remove("PWD");
        }
        else
        {
            environment["PWD"] = workingDirectory;
        }
    }
}
