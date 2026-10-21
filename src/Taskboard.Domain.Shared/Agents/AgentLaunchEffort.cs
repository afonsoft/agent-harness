namespace Taskboard.Agents;

/// <summary>
/// SPEC-20261020-v2 RF-008: shared map of launch <c>effort</c> strings to
/// <see cref="AgentModelTier"/> — used by the launch resolver (validation)
/// and by the dispatcher (effective launch).
/// </summary>
public static class AgentLaunchEffort
{
    private static readonly IReadOnlyDictionary<string, AgentModelTier> Tiers =
        new Dictionary<string, AgentModelTier>(StringComparer.OrdinalIgnoreCase)
        {
            ["low"] = AgentModelTier.Lite,
            ["lite"] = AgentModelTier.Lite,
            ["normal"] = AgentModelTier.Normal,
            ["medium"] = AgentModelTier.Normal,
            ["high"] = AgentModelTier.Ultra,
            ["ultra"] = AgentModelTier.Ultra,
            ["max"] = AgentModelTier.Ultra,
        };

    /// <summary>Returns null when <paramref name="effort"/> is unknown.</summary>
    public static AgentModelTier? ToTier(string? effort)
    {
        if (string.IsNullOrWhiteSpace(effort))
        {
            return null;
        }

        return Tiers.TryGetValue(effort.Trim(), out var tier) ? tier : null;
    }
}
