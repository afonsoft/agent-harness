using Microsoft.AspNetCore.SignalR;

namespace Taskboard.Server.Hubs;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-008: broadcast-only hub for chat
/// run lifecycle events. The dispatcher publishes <c>run.completed</c>
/// ({ runId, conversationId, title, status, error? }) through
/// <see cref="IHubContext{T}"/>; the Blazor <c>ChatNotificationsService</c>
/// subscribes for toast + Notification API fan-out. No join methods — every
/// connected client gets every notification (single-user local app).
/// </summary>
public sealed class ChatRunHub : Hub;
