namespace Taskboard.Integrations.Execution;

public static class WithoutHarnessEnv
{
    // Legacy TASKBOARD_* kept alongside HARNESS_* for the deprecation cycle.
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
