using Microsoft.AspNetCore.SignalR;

namespace Taskboard.Server.Hubs;

/// <summary>
/// SignalR hub streaming structured cockpit events per run
/// (SPEC-20260919-ade-cockpit-hitl §5). Clients join a run's group to receive
/// `ReceiveCockpitEvent(CockpitEventDto)` and `RequireApproval(ApprovalRequestDto)`;
/// the shared <c>runs</c> group (SPEC-20260923-cockpit-run-hardening RF-003)
/// carries the same events so the `/cockpit` list updates live.
/// </summary>
public sealed class HarnessCockpitHub : Hub
{
    /// <summary>Shared group every run event is also broadcast to.</summary>
    public const string RunsGroup = "runs";

    /// <summary>Subscribes the connection to a run's event group.</summary>
    public Task JoinRunGroup(string runId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, runId);

    /// <summary>Removes the connection from a run's event group.</summary>
    public Task LeaveRunGroup(string runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, runId);

    /// <summary>Subscribes the connection to the shared runs group (list page).</summary>
    public Task JoinRunsGroup() =>
        Groups.AddToGroupAsync(Context.ConnectionId, RunsGroup);

    /// <summary>Removes the connection from the shared runs group.</summary>
    public Task LeaveRunsGroup() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RunsGroup);
}
