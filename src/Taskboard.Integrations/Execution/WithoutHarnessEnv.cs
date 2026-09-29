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
}
