using System.Text.Json;
using Taskboard;
using Taskboard.Delegation;

namespace Taskboard.Domain.Entities.Delegation;

/// <summary>
/// SPEC-20261020-orchestration-contract-v2 RF-003: a FIFO batch of mailbox
/// messages handed to the coordinator inbox. Returned by <c>agent_check</c>
/// and replayed until <c>--ack</c> — read ≠ delivered.
/// </summary>
public sealed class Delivery : AggregateRoot<string>
{
    /// <summary>Max messages per batch (SPEC constant).</summary>
    public const int MaxMessages = 50;

    private Delivery(string id)
        : base(id)
    {
    }

    public string RunId { get; private set; } = string.Empty;
    public string MessagesJson { get; private set; } = "[]";
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? AckedAtUtc { get; private set; }

    public IReadOnlyList<string> MessageIds =>
        JsonSerializer.Deserialize<List<string>>(MessagesJson) ?? [];

    public static Delivery Create(
        string runId, IReadOnlyList<string> messageIds, DateTime? now = null)
    {
        if (messageIds.Count == 0)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "delivery needs at least one message");
        }

        if (messageIds.Count > MaxMessages)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"delivery batch exceeds {MaxMessages} messages");
        }

        return new Delivery($"del-{Guid.NewGuid():N}")
        {
            RunId = runId?.Trim() ?? string.Empty,
            MessagesJson = JsonSerializer.Serialize(messageIds),
            CreatedAtUtc = now ?? DateTime.UtcNow,
        };
    }

    public void Ack(DateTime? now = null) =>
        AckedAtUtc ??= now ?? DateTime.UtcNow;
}
