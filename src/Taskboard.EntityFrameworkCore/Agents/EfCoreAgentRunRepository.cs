using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Agents;

public sealed class EfCoreAgentRunRepository : IAgentRunRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreAgentRunRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<AgentRunDto> EnqueueAsync(string issueId, AgentType agentType, AgentModelTier? modelTier = null, string? modelName = null, CancellationToken cancellationToken = default)
    {
        var entity = new AgentRun(Guid.NewGuid(), issueId, agentType, DateTimeOffset.UtcNow, modelTier, modelName);
        await _context.AgentRuns.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task MarkRunningAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.AgentRuns.FindAsync([runId], cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        entity.MarkRunning();
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FinishAsync(Guid runId, AgentRunState finalState, CancellationToken cancellationToken = default)
    {
        var entity = await _context.AgentRuns.FindAsync([runId], cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        entity.MarkFinished(finalState, DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentRunDto>> GetByIssueIdAsync(string issueId, int take = 5, CancellationToken cancellationToken = default)
    {
        var runs = await _context.AgentRuns
            .Where(x => x.IssueId == issueId)
            .OrderByDescending(x => x.StartedAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return runs.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<AgentRunDto>> GetLatestPerIssueAsync(CancellationToken cancellationToken = default)
    {
        var latestIds = await _context.AgentRuns
            .GroupBy(x => x.IssueId)
            .Select(g => g.OrderByDescending(x => x.StartedAt).Select(x => x.Id).First())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var runs = await _context.AgentRuns
            .Where(x => latestIds.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return runs.Select(ToDto).ToList();
    }

    private static AgentRunDto ToDto(AgentRun run) =>
        new(run.Id, run.IssueId, run.AgentType, run.State, run.StartedAt, run.FinishedAt, run.ModelTier, run.ModelName);
}
