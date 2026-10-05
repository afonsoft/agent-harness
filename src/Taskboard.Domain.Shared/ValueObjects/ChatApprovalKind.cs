namespace Taskboard.ValueObjects;

/// <summary>
/// What a <c>ChatApproval</c> gates (SPEC-20261005-chat-plan-mode RF-003):
/// <c>tool-call</c> is a mutating tool execution;
/// <c>plan-review</c> is an <c>exit_plan_mode</c> plan asking for sign-off.
/// </summary>
public sealed record ChatApprovalKind : StringValueObject
{
    private static readonly IReadOnlyCollection<string> AllowedValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "tool-call",
        "plan-review",
    };

    public static readonly ChatApprovalKind ToolCall = new("tool-call");
    public static readonly ChatApprovalKind PlanReview = new("plan-review");

    public ChatApprovalKind(string value)
        : base(value, AllowedValues)
    {
    }

    public static ChatApprovalKind From(string value) => new(value);
}
