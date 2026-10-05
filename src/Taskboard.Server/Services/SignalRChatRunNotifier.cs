using Microsoft.AspNetCore.SignalR;
using Taskboard.Application.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;
using Taskboard.Server.Hubs;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-008: <see cref="IChatRunNotifier"/>
/// over SignalR — the dispatcher calls <see cref="RunCompletedAsync"/> in its
/// per-run scope; this fans out <c>run.completed</c> to every connected
/// client with the conversation title the toast/notification needs.
/// </summary>
public sealed class SignalRChatRunNotifier(
    IHubContext<ChatRunHub> hubContext,
    IRepository<ChatConversation> conversations) : IChatRunNotifier
{
    public const string RunCompletedEvent = "run.completed";

    public async Task RunCompletedAsync(ChatRun run, CancellationToken ct = default)
    {
        var conversation = await conversations
            .GetAsync(run.ConversationId, ct)
            .ConfigureAwait(false);

        await hubContext.Clients.All.SendAsync(
            RunCompletedEvent,
            new
            {
                runId = run.Id.Value,
                conversationId = run.ConversationId.Value,
                title = conversation?.Title,
                status = run.Status.Value,
                error = run.Error,
            },
            ct).ConfigureAwait(false);
    }
}
