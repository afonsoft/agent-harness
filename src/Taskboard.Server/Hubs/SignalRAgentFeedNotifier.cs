using Microsoft.AspNetCore.SignalR;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Server.Hubs;

/// <summary>
/// SPEC-20261020-agents-feed-notifications RF-010: delivers
/// <c>agent.feed</c> pushes to the user's SignalR group.
/// </summary>
public sealed class SignalRAgentFeedNotifier : IAgentFeedNotifier
{
    private readonly IHubContext<AgentFeedHub> _hub;

    public SignalRAgentFeedNotifier(IHubContext<AgentFeedHub> hub)
    {
        _hub = hub;
    }

    public Task PushAsync(string userId, AgentFeedPushDto push, CancellationToken ct = default) =>
        _hub.Clients.Group(IAgentFeedNotifier.GroupFor(userId))
            .SendAsync("agent.feed", push, ct);
}
