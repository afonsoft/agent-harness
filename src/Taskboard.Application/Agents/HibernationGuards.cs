using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261020-agent-hibernation RF-003/RF-004: the CLI must offer a native
/// resume — devin/antigravity/unknown never hibernate.
/// </summary>
public sealed class ResumeSupportGuard : IAgentHibernationGuard
{
    public string Name => "resume-support";

    public Task<string?> CheckAsync(HibernationContext ctx, CancellationToken ct = default) =>
        Task.FromResult(
            AgentCliResumeSupport.SupportsResume(ctx.State.Cli)
                ? null
                : $"cli '{ctx.State.Cli}' has no native resume");
}

/// <summary>RF-003: sessions blocked on the human never hibernate.</summary>
public sealed class AttentionGuard : IAgentHibernationGuard
{
    public string Name => "attention";

    public Task<string?> CheckAsync(HibernationContext ctx, CancellationToken ct = default) =>
        Task.FromResult<string?>(ctx.State.State switch
        {
            AgentAttentionState.WaitingYou => "session is waiting on you",
            AgentAttentionState.Blocked => "session is blocked",
            _ => null,
        });
}

/// <summary>RF-009: pinned ("keep alive") sessions never hibernate.</summary>
public sealed class PinnedGuard : IAgentHibernationGuard
{
    public string Name => "pinned";

    public Task<string?> CheckAsync(HibernationContext ctx, CancellationToken ct = default) =>
        Task.FromResult(ctx.Record?.Pinned == true ? "session is pinned" : null);
}

/// <summary>
/// RF-003: an open dispatch bound to the session's terminal handle means an
/// unsettled run — hibernating would orphan the attempt.
/// </summary>
public sealed class OpenDispatchGuard(IDispatchRepository dispatches) : IAgentHibernationGuard
{
    public string Name => "open-dispatch";

    public async Task<string?> CheckAsync(HibernationContext ctx, CancellationToken ct = default) =>
        (await dispatches.ListOpenForSessionAsync(ctx.State.SessionKey, ct).ConfigureAwait(false))
            .Count > 0
            ? "unsettled dispatch bound to session"
            : null;
}
