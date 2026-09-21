using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Application.Agents;

/// <summary>
/// Reconciles <see cref="AgentRunState.Queued"/>/<see cref="AgentRunState.Running"/>
/// rows against the orchestrator's live run set — a run with no live job past
/// the threshold is an orphan (server restart, dead process) and is finished
/// as <see cref="AgentRunState.Failed"/> (SPEC-20260920-harness-maintenance-jobs
/// RF-001). Called every 5min by <c>StaleAgentRunReaperService</c>.
/// </summary>
public sealed class StaleRunReaper
{
    private readonly IAgentRunRepository _runs;

    public StaleRunReaper(IAgentRunRepository runs)
    {
        _runs = runs;
    }

    /// <summary>Finishes stale runs absent from <paramref name="liveRunIds"/>. Returns the reaped ids.</summary>
    public async Task<IReadOnlyList<Guid>> RunOnceAsync(
        IReadOnlyCollection<Guid> liveRunIds,
        TimeSpan threshold,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - threshold;
        var candidates = await _runs.GetStaleActiveRunsAsync(cutoffUtc: cutoff, cancellationToken)
            .ConfigureAwait(false);

        var live = new HashSet<Guid>(liveRunIds);
        var reaped = new List<Guid>();
        foreach (var run in candidates)
        {
            if (live.Contains(run.Id))
            {
                continue; // queued/running right now — pickup lag is not staleness
            }

            await _runs.FinishAsync(run.Id, AgentRunState.Failed, CancellationToken.None)
                .ConfigureAwait(false);
            reaped.Add(run.Id);
        }

        return reaped;
    }
}
