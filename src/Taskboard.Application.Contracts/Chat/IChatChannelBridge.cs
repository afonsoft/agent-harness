namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261025 C1: one inbound message from an external chat channel
/// (Slack). <see cref="ExternalThreadId"/> is the channel's thread anchor —
/// Slack's <c>thread_ts</c> (or the message's own <c>ts</c> for roots).
/// </summary>
public sealed record ChatChannelInboundMessage(
    string Channel,
    string ExternalChannelId,
    string ExternalThreadId,
    string? ExternalUserId,
    string? ExternalUserName,
    string Text);

/// <summary>
/// Channel adapter ↔ chat service seam. Channel adapters (Slack socket,
/// Discord…) call <see cref="InboundMessageAsync"/> for user text and
/// <see cref="DecideApprovalAsync"/> for approve/reject actions — the
/// decision lands on the same <c>ChatApproval</c> row a UI click would
/// touch, with <c>channel:&lt;name&gt;</c> + actor in the audit fields.
/// </summary>
public interface IChatChannelBridge
{
    /// <summary>
    /// Routes the message into the bound conversation (creating one — and
    /// the binding — on first contact). Returns the conversation id.
    /// </summary>
    Task<string> InboundMessageAsync(
        ChatChannelInboundMessage message, CancellationToken cancellationToken = default);

    /// <summary>Answers a pending approval as a channel-side human actor.</summary>
    Task<ChatApprovalDto?> DecideApprovalAsync(
        string approvalId, string outcome, string channel, string? actor,
        string? reason, CancellationToken cancellationToken = default);
}
