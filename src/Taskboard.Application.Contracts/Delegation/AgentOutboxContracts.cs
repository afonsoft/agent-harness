namespace Taskboard.Application.Contracts.Delegation;

/// <summary>
/// SPEC-20261104-agent-lineage-notes RF-005: durable copy of a mailbox message
/// queued for a recipient session that is hibernated or detached. Entries are
/// an outbox <em>index</em> — the message itself stays in the mailbox, so a
/// flush only clears the queue row and the normal delivery/ack flow delivers
/// the payload (at-least-once preserved).
/// </summary>
public sealed record AgentOutboxEntryDto(
    string Id,
    string SessionKey,
    string MessageId,
    string Kind,
    DateTime CreatedAtUtc);

/// <summary>Persistence boundary for <c>AgentOutboxEntries</c>.</summary>
public interface IAgentOutboxRepository
{
    Task<AgentOutboxEntryDto> EnqueueAsync(
        string sessionKey, string messageId, string kind, DateTime now,
        CancellationToken ct = default);

    /// <summary>Pending entries for a session, oldest first.</summary>
    Task<IReadOnlyList<AgentOutboxEntryDto>> ListPendingAsync(
        string sessionKey, int take = 200, CancellationToken ct = default);

    Task<int> CountBySessionAsync(string sessionKey, CancellationToken ct = default);

    /// <summary>
    /// Overflow policy: while the session exceeds <paramref name="capacity"/>,
    /// deletes the oldest entries whose kind is not in
    /// <paramref name="protectedKinds"/> (notes/worker_done never drop);
    /// returns the number of dropped rows.
    /// </summary>
    Task<int> TrimToCapacityAsync(
        string sessionKey, int capacity, IReadOnlyCollection<string> protectedKinds,
        CancellationToken ct = default);

    /// <summary>Flushes every pending entry for a session; returns the count.</summary>
    Task<int> FlushAsync(string sessionKey, CancellationToken ct = default);

    /// <summary>
    /// Deletes the listed entries (delivered to a live terminal, or orphans
    /// whose mailbox message is already gone); returns the removed count.
    /// </summary>
    Task<int> RemoveAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default);
}
