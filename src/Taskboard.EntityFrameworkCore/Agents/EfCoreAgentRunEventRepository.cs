using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Agents;

public sealed class EfCoreAgentRunEventRepository : IAgentRunEventRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreAgentRunEventRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task AppendAsync(AgentExecutionEvent evt, CancellationToken cancellationToken = default)
    {
        await _context.AgentRunEvents.AddAsync(AgentRunEvent.From(evt), cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetMaxSequenceAsync(string scopeKind, string scopeId, CancellationToken cancellationToken = default)
    {
        return await _context.AgentRunEvents
            .Where(x => x.ScopeKind == scopeKind && x.ScopeId == scopeId)
            .Select(x => (long?)x.Sequence)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0L;
    }

    public async Task<IReadOnlyList<AgentExecutionEvent>> GetPageAsync(
        string scopeKind, string scopeId, long after, int take, CancellationToken cancellationToken = default)
    {
        var rows = await _context.AgentRunEvents
            .Where(x => x.ScopeKind == scopeKind && x.ScopeId == scopeId && x.Sequence > after)
            .OrderBy(x => x.Sequence)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r => r.ToEvent()).ToList();
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
    {
        return await _context.AgentRunEvents
            .Where(x => x.TimestampUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
