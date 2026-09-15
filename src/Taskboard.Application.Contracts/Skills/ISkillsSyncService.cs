using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Skills;

/// <summary>
/// Synchronizes the skills from the configured source repository into the
/// skills directories of the enabled agent CLIs. Synchronization is additive:
/// skills not present in the source repository are never removed.
/// </summary>
public interface ISkillsSyncService
{
    /// <summary>Returns the last known synchronization snapshot.</summary>
    SkillsSyncStatus GetStatus();

    /// <summary>
    /// Starts a synchronization in the background. Requests made while a run is
    /// already in flight are coalesced — the in-flight run is not duplicated.
    /// </summary>
    /// <param name="agents">
    /// Agents to synchronize. When <c>null</c>, all enabled agents are resolved.
    /// </param>
    void RequestSync(IReadOnlyCollection<AgentType>? agents = null);

    /// <summary>
    /// Runs a synchronization. When a run is already in flight, returns the
    /// in-flight status snapshot instead of queueing a second run.
    /// </summary>
    /// <param name="agents">
    /// Agents to synchronize. When <c>null</c>, all enabled agents are resolved.
    /// </param>
    Task<SkillsSyncStatus> SyncAsync(
        IReadOnlyCollection<AgentType>? agents = null,
        CancellationToken cancellationToken = default);
}
