using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>
/// SPEC-20261025 C1: binds an external chat channel endpoint (Slack
/// channel + thread) to a provider-chat <see cref="ConversationId"/> — one
/// row per (Channel, ExternalChannelId, ExternalThreadId). Inbound channel
/// messages land as user messages on the bound conversation; outbound
/// approvals post back to the bound channel/thread.
/// </summary>
public sealed class ChatChannelBinding : Entity<ChatChannelBindingId>
{
    /// <summary>Adapter id — <c>"slack"</c> first; discord later.</summary>
    public string Channel { get; private set; } = default!;

    /// <summary>Channel-side container id (Slack channel id, e.g. <c>C0123ABC</c>).</summary>
    public string ExternalChannelId { get; private set; } = default!;

    /// <summary>
    /// Channel-side thread anchor — the Slack root message ts. Replies in the
    /// thread share it; an empty string would mean "whole channel" (unused).
    /// </summary>
    public string ExternalThreadId { get; private set; } = default!;

    /// <summary>Channel-side user who opened the thread — informational.</summary>
    public string? ExternalUserId { get; private set; }

    public ChatConversationId ConversationId { get; private set; } = default!;

    public DateTime CreatedAt { get; private set; }

    private ChatChannelBinding()
    {
    }

    private ChatChannelBinding(
        ChatChannelBindingId id, string channel, string externalChannelId,
        string externalThreadId, string? externalUserId,
        ChatConversationId conversationId, DateTime createdAt)
        : base(id)
    {
        Channel = channel;
        ExternalChannelId = externalChannelId;
        ExternalThreadId = externalThreadId;
        ExternalUserId = externalUserId;
        ConversationId = conversationId;
        CreatedAt = createdAt;
    }

    public static ChatChannelBinding Create(
        ChatChannelBindingId id, string channel, string externalChannelId,
        string externalThreadId, string? externalUserId,
        ChatConversationId conversationId, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Channel is required.");
        }
        if (string.IsNullOrWhiteSpace(externalChannelId))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "External channel id is required.");
        }
        if (string.IsNullOrWhiteSpace(externalThreadId))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "External thread id is required.");
        }

        return new(id, channel.Trim(), externalChannelId.Trim(), externalThreadId.Trim(),
            string.IsNullOrWhiteSpace(externalUserId) ? null : externalUserId.Trim(),
            conversationId, now ?? DateTime.UtcNow);
    }
}
