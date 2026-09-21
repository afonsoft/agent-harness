using Microsoft.AspNetCore.SignalR;

namespace Taskboard.Server.Hubs;

/// <summary>
/// SignalR hub streaming structured cockpit events per run
/// (SPEC-20260919-ade-cockpit-hitl §5). Clients join a run's group to receive
/// `ReceiveCockpitEvent(CockpitEventDto)` and `RequireApproval(ApprovalRequestDto)`.
/// </summary>
public sealed class HarnessCockpitHub : Hub
{
    /// <summary>Subscribes the connection to a run's event group.</summary>
    public Task JoinRunGroup(string runId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, runId);

    /// <summary>Removes the connection from a run's event group.</summary>
    public Task LeaveRunGroup(string runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, runId);
}
