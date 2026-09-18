using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Agents;

public sealed class EfCoreAgentLogRepository : IAgentLogRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreAgentLogRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task AppendAsync(AgentLogMessage log, CancellationToken cancellationToken = default)
    {
        var entity = new AgentLog(log.Timestamp, log.IssueId, log.Stream, log.Content);
        await _context.AgentLogs.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentLogMessage>> GetByIssueIdAsync(string issueId, CancellationToken cancellationToken = default)
    {
        var logs = await _context.AgentLogs
            .Where(x => x.IssueId == issueId)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return logs.Select(l => new AgentLogMessage(l.Timestamp, l.IssueId, l.Stream, l.Content)).ToList();
    }

    public async Task DeleteByIssueIdAsync(string issueId, CancellationToken cancellationToken = default)
    {
        await _context.AgentLogs
            .Where(x => x.IssueId == issueId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
