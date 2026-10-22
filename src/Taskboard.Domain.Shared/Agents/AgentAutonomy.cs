namespace Taskboard.Agents;

/// <summary>
/// SPEC-20261022-cli-autonomy-profiles RF-001: the permission envelope a
/// headless agent run gets. Ordered — a higher level subsumes the lower ones.
/// <list type="bullet">
/// <item><see cref="Plan"/> — read-only/research: no write or exec approval is
/// granted (per-CLI flag when one exists, otherwise prompt-contract only).</item>
/// <item><see cref="Edit"/> — today's default headless behavior: workspace
/// writes and tool use auto-approved inside the run directory.</item>
/// <item><see cref="Full"/> — unrestricted; allowed only inside a disposable
/// worktree/isolation session and behind <c>Taskboard:Agents:AllowFullAutonomy</c>.</item>
/// </list>
/// </summary>
public enum AgentAutonomy
{
    Plan = 0,
    Edit = 1,
    Full = 2,
}

public static class AgentAutonomyNames
{
    public const string Plan = "plan";
    public const string Edit = "edit";
    public const string Full = "full";

    public static string Name(AgentAutonomy level) => level switch
    {
        AgentAutonomy.Plan => Plan,
        AgentAutonomy.Edit => Edit,
        AgentAutonomy.Full => Full,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    public static bool TryParse(string? value, out AgentAutonomy level)
    {
        level = AgentAutonomy.Edit;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "":
                return true; // absent → default
            case Plan:
                level = AgentAutonomy.Plan;
                return true;
            case Edit:
                level = AgentAutonomy.Edit;
                return true;
            case Full:
                level = AgentAutonomy.Full;
                return true;
            default:
                return false;
        }
    }

    public static bool IsValidName(string? value) => value is Plan or Edit or Full;
}
