using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Domain.Entities.Delegation;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Delegation;

/// <summary>
/// EF Core store of <see cref="AgentOutboxEntry"/> — the durable outbox index
/// for mailbox messages addressed to detached/hibernated sessions
/// (SPEC-20261104-agent-lineage-notes RF-005).
/// </summary>
public sealed class EfCoreAgentOutboxRepository : IAgentOutboxRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreAgentOutboxRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<AgentOutboxEntryDto> EnqueueAsync(
        string sessionKey, string messageId, string kind, DateTime now,
        CancellationToken ct = default)
    {
        var entity = AgentOutboxEntry.Enqueue(sessionKey, messageId, kind, now);
        _context.AgentOutboxEntries.Add(entity);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<IReadOnlyList<AgentOutboxEntryDto>> ListPendingAsync(
        string sessionKey, int take = 200, CancellationToken ct = default) =>
        await _context.AgentOutboxEntries
            .Where(e => e.SessionKey == sessionKey)
            .OrderBy(e => e.CreatedAtUtc)
            .ThenBy(e => e.Id)
            .Take(take)
            .Select(e => ToDto(e))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<int> CountBySessionAsync(string sessionKey, CancellationToken ct = default) =>
        await _context.AgentOutboxEntries
            .CountAsync(e => e.SessionKey == sessionKey, ct)
            .ConfigureAwait(false);

    public async Task<int> TrimToCapacityAsync(
        string sessionKey, int capacity, IReadOnlyCollection<string> protectedKinds,
        CancellationToken ct = default)
    {
        var overflow = await _context.AgentOutboxEntries
            .CountAsync(e => e.SessionKey == sessionKey, ct)
            .ConfigureAwait(false) - capacity;
        if (overflow <= 0)
        {
            return 0;
        }

        var droppable = await _context.AgentOutboxEntries
            .Where(e => e.SessionKey == sessionKey && !protectedKinds.Contains(e.Kind))
            .OrderBy(e => e.CreatedAtUtc)
            .ThenBy(e => e.Id)
            .Take(overflow)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (droppable.Count == 0)
        {
            return 0;
        }

        _context.AgentOutboxEntries.RemoveRange(droppable);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return droppable.Count;
    }

    public async Task<int> FlushAsync(string sessionKey, CancellationToken ct = default)
    {
        var pending = await _context.AgentOutboxEntries
            .Where(e => e.SessionKey == sessionKey)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return 0;
        }

        _context.AgentOutboxEntries.RemoveRange(pending);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return pending.Count;
    }

    public async Task<int> RemoveAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        var stale = await _context.AgentOutboxEntries
            .Where(e => ids.Contains(e.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (stale.Count == 0)
        {
            return 0;
        }

        _context.AgentOutboxEntries.RemoveRange(stale);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return stale.Count;
    }

    private static AgentOutboxEntryDto ToDto(AgentOutboxEntry e) =>
        new(e.Id, e.SessionKey, e.MessageId, e.Kind, e.CreatedAtUtc);
}
