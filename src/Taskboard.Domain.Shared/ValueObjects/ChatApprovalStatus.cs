namespace Taskboard.ValueObjects;

/// <summary>
/// Lifecycle of a provider-chat tool approval (SPEC-20261005-chat-tool-approval
/// RF-001): pending → allowed-once | rejected | cancelled | unavailable.
/// <c>unavailable</c> is the fail-closed outcome (timeout/policy error) —
/// never allow.
/// </summary>
public sealed record ChatApprovalStatus : StringValueObject
{
    private static readonly IReadOnlyCollection<string> AllowedValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "pending",
        "allowed-once",
        "rejected",
        "cancelled",
        "unavailable",
    };

    public static readonly ChatApprovalStatus Pending = new("pending");
    public static readonly ChatApprovalStatus AllowedOnce = new("allowed-once");
    public static readonly ChatApprovalStatus Rejected = new("rejected");
    public static readonly ChatApprovalStatus Cancelled = new("cancelled");
    public static readonly ChatApprovalStatus Unavailable = new("unavailable");

    public ChatApprovalStatus(string value)
        : base(value, AllowedValues)
    {
    }

    public static ChatApprovalStatus From(string value) => new(value);

    /// <summary>True while the approval still awaits a decision.</summary>
    public bool IsPending => this == Pending;
}

/// <summary>Who decided a chat tool approval (RF-001 audit).</summary>
public sealed record ChatApprovalDecidedBy : StringValueObject
{
    private static readonly IReadOnlyCollection<string> AllowedValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "ui",
        "auto-timeout",
        "auto-cancel",
    };

    public static readonly ChatApprovalDecidedBy Ui = new("ui");
    public static readonly ChatApprovalDecidedBy AutoTimeout = new("auto-timeout");
    public static readonly ChatApprovalDecidedBy AutoCancel = new("auto-cancel");

    public ChatApprovalDecidedBy(string value)
        : base(value, AllowedValues)
    {
    }

    public static ChatApprovalDecidedBy From(string value) => new(value);
}
