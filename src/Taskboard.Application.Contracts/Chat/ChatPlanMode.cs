using Microsoft.Extensions.Configuration;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Plan-mode policy knobs (SPEC-20261005-chat-plan-mode RNF-003): the
/// prompt section text and the new-conversation default are config-owned so
/// deployments can reword/opt-in without a rebuild.
/// </summary>
public static class ChatPlanMode
{
    public const string SectionKey = "Taskboard:Chat:PlanMode:Section";
    public const string DefaultKey = "Taskboard:Chat:PlanMode:Default";

    /// <summary>The <c>exit_plan_mode</c> tool — the plan-review gate key.</summary>
    public const string ToolName = "exit_plan_mode";

    /// <summary>
    /// Fallback <c>plan:policy</c> section appended to the system prompt
    /// while plan mode is on (RF-002).
    /// </summary>
    public const string SectionDefault =
        "Plan mode is active. Do not mutate anything: inspect read-only and "
        + "produce a plan instead. When the plan is ready, present it by "
        + "calling exit_plan_mode — the user reviews it before any execution.";

    /// <summary>Synthetic refusal for a mutating call while planning (RF-002).</summary>
    public const string MutatingDenial =
        "Plan mode is active: mutations are not allowed. "
        + "Present a plan via exit_plan_mode.";

    /// <summary>Prompt section text — config override or the built-in default.</summary>
    public static string Section(IConfiguration configuration)
    {
        var raw = configuration[SectionKey];
        return string.IsNullOrWhiteSpace(raw) ? SectionDefault : raw.Trim();
    }

    /// <summary>Whether new conversations start in plan mode (default off).</summary>
    public static bool DefaultOn(IConfiguration configuration)
    {
        var raw = configuration[DefaultKey];
        return bool.TryParse(raw, out var on) && on;
    }
}
