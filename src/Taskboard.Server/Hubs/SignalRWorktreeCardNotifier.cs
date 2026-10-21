using Microsoft.AspNetCore.SignalR;
using Taskboard.Application.Contracts.Harness;

namespace Taskboard.Server.Hubs;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-008: delivers <c>worktree.card</c>
/// patches to the cockpit hub's shared runs group — the board and worktree
/// detail update live (P3 renders them).
/// </summary>
public sealed class SignalRWorktreeCardNotifier : IWorktreeCardNotifier
{
    private readonly IHubContext<HarnessCockpitHub> _hub;

    public SignalRWorktreeCardNotifier(IHubContext<HarnessCockpitHub> hub)
    {
        _hub = hub;
    }

    public Task PushAsync(WorktreeCardDto card, CancellationToken ct = default) =>
        _hub.Clients.Group(HarnessCockpitHub.RunsGroup)
            .SendAsync("worktree.card", card, ct);
}
