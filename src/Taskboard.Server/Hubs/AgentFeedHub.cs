using Microsoft.AspNetCore.SignalR;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Server.Hubs;

/// <summary>
/// SPEC-20261020-agents-feed-notifications RF-010: SignalR push for the global
/// agents feed. One group per user (<c>feed:{userId}</c>) carries
/// <c>agent.feed</c> events — <see cref="AgentFeedPushDto"/> with a new/merged
/// item or a read-state patch plus current unread counts. Clients re-fetch
/// from their saved cursor after reconnect.
/// </summary>
public sealed class AgentFeedHub : Hub
{
    /// <summary>Subscribes the connection to a user's feed group.</summary>
    public Task JoinFeedGroup(string userId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, IAgentFeedNotifier.GroupFor(userId));

    /// <summary>Removes the connection from a user's feed group.</summary>
    public Task LeaveFeedGroup(string userId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, IAgentFeedNotifier.GroupFor(userId));
}
