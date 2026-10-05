using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>
/// One pending tool-execution question of a provider-chat run
/// (SPEC-20261005-chat-tool-approval RF-001). Persisted so a detached run can
/// surface the question on any tab, replay it on re-attach, and audit the
/// decision (<see cref="DecidedAt"/>/<see cref="DecidedBy"/>).
/// </summary>
public sealed class ChatApproval : AggregateRoot<ChatApprovalId>
{
    /// <summary>Max length of <see cref="ArgumentsPreview"/> — args JSON truncated.</summary>
    public const int ArgumentsPreviewMaxLength = 2000;

    /// <summary>
    /// Max length of <see cref="ArgumentsPreview"/> on a
    /// <see cref="ChatApprovalKind.PlanReview"/> row — the plan markdown must
    /// survive intact for review/export (RF-003/P2).
    /// </summary>
    public const int PlanPreviewMaxLength = 16000;

    public ChatRunId RunId { get; private set; } = default!;

    public ChatConversationId ConversationId { get; private set; } = default!;

    /// <summary>The OpenAI tool_call id the approval gates — used to match the call.</summary>
    public string ToolCallId { get; private set; } = default!;

    public string ToolName { get; private set; } = default!;

    /// <summary>Arguments JSON capped at <see cref="ArgumentsPreviewMaxLength"/> chars.</summary>
    public string ArgumentsPreview { get; private set; } = default!;

    /// <summary>
    /// What this approval gates (SPEC-20261005-chat-plan-mode RF-003) —
    /// <c>tool-call</c> or <c>plan-review</c>. The decide endpoint serves both;
    /// the UI renders the matching card.
    /// </summary>
    public ChatApprovalKind Kind { get; private set; } = ChatApprovalKind.ToolCall;

    public ChatApprovalStatus Status { get; private set; } = ChatApprovalStatus.Pending;

    public DateTime RequestedAt { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    /// <summary>Decision payload — outcome plus optional reason (e.g. "allow" / "deny: too risky").</summary>
    public string? Decision { get; private set; }

    public ChatApprovalDecidedBy? DecidedBy { get; private set; }

    private ChatApproval()
    {
    }

    private ChatApproval(
        ChatApprovalId id,
        ChatRunId runId,
        ChatConversationId conversationId,
        string toolCallId,
        string toolName,
        string argumentsPreview,
        DateTime requestedAt,
        ChatApprovalKind kind)
        : base(id)
    {
        RunId = runId;
        ConversationId = conversationId;
        ToolCallId = toolCallId;
        ToolName = toolName;
        ArgumentsPreview = argumentsPreview;
        RequestedAt = requestedAt;
        Kind = kind;
    }

    public static ChatApproval Create(
        ChatApprovalId id,
        ChatRunId runId,
        ChatConversationId conversationId,
        string toolCallId,
        string toolName,
        string? argumentsJson,
        DateTime? now = null,
        ChatApprovalKind? kind = null)
    {
        var resolvedKind = kind ?? ChatApprovalKind.ToolCall;
        var preview = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
        var cap = resolvedKind == ChatApprovalKind.PlanReview
            ? PlanPreviewMaxLength
            : ArgumentsPreviewMaxLength;
        if (preview.Length > cap)
        {
            preview = preview[..cap];
        }

        return new ChatApproval(id, runId, conversationId, toolCallId, toolName, preview, now ?? DateTime.UtcNow, resolvedKind);
    }

    /// <summary>
    /// pending → terminal. Throws when the row already left
    /// <see cref="ChatApprovalStatus.Pending"/> — the decide endpoint maps it
    /// to 409 (atomic race-safe answer, RF-003).
    /// </summary>
    public void Decide(ChatApprovalStatus outcome, string? decision, ChatApprovalDecidedBy decidedBy, DateTime? now = null)
    {
        if (outcome.IsPending)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Approval '{Id}' cannot decide to 'pending'.");
        }

        EnsurePending(nameof(Decide));
        Status = outcome;
        Decision = decision;
        DecidedBy = decidedBy;
        DecidedAt = now ?? DateTime.UtcNow;
        IncrementVersion();
    }

    /// <summary>Convenience for a UI answer: allow or deny.</summary>
    public void DecideFromUi(bool allow, string? reason, DateTime? now = null) =>
        Decide(
            allow ? ChatApprovalStatus.AllowedOnce : ChatApprovalStatus.Rejected,
            ComposeDecision(allow ? "allow" : "deny", reason),
            ChatApprovalDecidedBy.Ui,
            now);

    /// <summary>Run stop/host shutdown while pending → cancelled (fail-closed).</summary>
    public void Cancel(DateTime? now = null)
    {
        if (!Status.IsPending)
        {
            return; // already decided — stop is a no-op, not an error.
        }

        Status = ChatApprovalStatus.Cancelled;
        Decision = "cancelled";
        DecidedBy = ChatApprovalDecidedBy.AutoCancel;
        DecidedAt = now ?? DateTime.UtcNow;
        IncrementVersion();
    }

    /// <summary>Timeout/no answerer → unavailable (fail-closed deny, RNF-002).</summary>
    public void Expire(DateTime? now = null)
    {
        if (!Status.IsPending)
        {
            return;
        }

        Status = ChatApprovalStatus.Unavailable;
        Decision = "timeout";
        DecidedBy = ChatApprovalDecidedBy.AutoTimeout;
        DecidedAt = now ?? DateTime.UtcNow;
        IncrementVersion();
    }

    /// <summary>True when the decision lets the gated call run exactly once.</summary>
    public bool IsAllowedOnce => Status == ChatApprovalStatus.AllowedOnce;

    private void EnsurePending(string transition)
    {
        if (!Status.IsPending)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Approval '{Id}' cannot {transition} from status '{Status}'.");
        }
    }

    private static string ComposeDecision(string outcome, string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? outcome : $"{outcome}: {reason.Trim()}";
}
