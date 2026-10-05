using System.Text.Json;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// SPEC-20261005-chat-plan-mode RF-003: the model's way out of plan mode —
/// presents a markdown plan for review. The approval gate intercepts the
/// call BEFORE this executes: a persisted <c>plan-review</c> ChatApproval
/// parks the run until the user approves or sends feedback, so reaching
/// <see cref="ExecuteAsync"/> already means "approved".
/// </summary>
public sealed class ExitPlanModeTool : IChatTool
{
    public string Name => ChatPlanMode.ToolName;

    public string Description =>
        "Present the finished plan for user review and leave plan mode once "
        + "approved. 'plan' is a markdown document starting with a '#' title — "
        + "the call suspends until the user approves or replies with feedback.";

    public string ParametersJson =>
        """{"type":"object","properties":{"plan":{"type":"string","description":"Markdown plan starting with a '#' title"}},"required":["plan"]}""";

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var plan = arguments.TryGetProperty("plan", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(plan) || !plan.TrimStart().StartsWith('#'))
        {
            return Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new
                {
                    error = "exit_plan_mode requires a non-empty 'plan' markdown argument starting with '#'.",
                }), Refused: true, "invalid plan"));
        }

        return Task.FromResult(new ChatToolResult(
            JsonSerializer.Serialize(new
            {
                approved = true,
                message = "Plan approved — plan mode is off; proceed to implement it.",
            })));
    }
}
