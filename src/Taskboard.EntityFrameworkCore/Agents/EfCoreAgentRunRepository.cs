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
        // SPEC-20261003-perf-pass RF-002: StartedAt is stored as INTEGER unix-ms
        // (AgentRunConfiguration converter), so MAX + join translates to a
        // single SQL round-trip on SQLite — the old group-select-First +
        // Contains pattern was two queries with provider-fragile translation.
        var latest = _context.AgentRuns
            .GroupBy(x => x.IssueId)
            .Select(g => new { IssueId = g.Key, MaxStartedAt = g.Max(x => x.StartedAt) });

        var runs = await _context.AgentRuns
            .Join(latest,
                run => new { run.IssueId, run.StartedAt },
                l => new { l.IssueId, StartedAt = l.MaxStartedAt },
                (run, _) => run)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A tie on IssueId+StartedAt would join more than one row per issue —
        // collapse to one so the "latest run per issue" contract holds
        // (previously the group-First pick chose a single arbitrary row).
        return runs
            .GroupBy(x => x.IssueId)
            .Select(g => ToDto(g.First()))
            .ToList();
    }

    public async Task<IReadOnlyList<AgentRunDto>> GetStaleActiveRunsAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
    {
        // SPEC-20261003-perf-pass RF-003: the StartedAt converter maps the
        // parameter to unix-ms too, so the cutoff now filters in SQL
        // (previously every active run was loaded then filtered in memory).
        var runs = await _context.AgentRuns
            .Where(x => x.State == AgentRunState.Queued || x.State == AgentRunState.Running)
            .Where(x => x.StartedAt < cutoffUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return runs.Select(ToDto).ToList();
    }

    private static AgentRunDto ToDto(AgentRun run) =>
        new(run.Id, run.IssueId, run.AgentType, run.State, run.StartedAt, run.FinishedAt, run.ModelTier, run.ModelName);
}
