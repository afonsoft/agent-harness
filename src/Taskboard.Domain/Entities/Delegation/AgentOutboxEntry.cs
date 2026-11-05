using Taskboard;

namespace Taskboard.Domain.Entities.Delegation;

/// <summary>
/// SPEC-20261104-agent-lineage-notes RF-005: queued copy of a mailbox message
/// addressed to a recipient session that is hibernated or detached. Flushed
/// oldest-first on resume/attach through the Delivery machinery — at-least-once
/// is preserved by the ack flow downstream.
/// </summary>
public sealed class AgentOutboxEntry : AggregateRoot<string>
{
    private AgentOutboxEntry(string id)
        : base(id)
    {
    }

    /// <summary>Recipient session key the entry waits for.</summary>
    public string SessionKey { get; private set; } = string.Empty;

    /// <summary>Source mailbox message id.</summary>
    public string MessageId { get; private set; } = string.Empty;

    /// <summary>Message kind — ordering/drop priority uses it.</summary>
    public string Kind { get; private set; } = "text";

    public DateTime CreatedAtUtc { get; private set; }

    public static AgentOutboxEntry Enqueue(
        string sessionKey, string messageId, string kind, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "sessionKey is required");
        }

        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "messageId is required");
        }

        return new AgentOutboxEntry($"ob-{Guid.NewGuid():N}")
        {
            SessionKey = sessionKey.Trim(),
            MessageId = messageId.Trim(),
            Kind = string.IsNullOrWhiteSpace(kind) ? "text" : kind.Trim(),
            CreatedAtUtc = now ?? DateTime.UtcNow,
        };
    }
}
