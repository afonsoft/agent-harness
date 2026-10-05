using System.Text.Json;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Chat;
using Taskboard.Application.Configuration;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-009: <see cref="IChatRunNotifier"/>
/// over Web Push — delivers <c>run.completed</c> to every stored subscription
/// when the global push toggle is on. This is the channel that reaches a
/// fully-closed browser (service-worker push). Dead endpoints (404/410) are
/// pruned per RFC 8030 §7.3.
/// </summary>
public sealed class WebPushChatRunNotifier(
    IRepository<ChatPushSubscription> subscriptions,
    IRepository<ChatConversation> conversations,
    IWebPushSender sender,
    RuntimeConfigurationService configuration,
    ILogger<WebPushChatRunNotifier> logger) : IChatRunNotifier
{
    internal const string PushToggleKey = "Taskboard:Chat:Notify:Done:Push";

    /// <summary>SPEC-20261005-chat-tool-approval open-Q3/P2: opt-in push when a run parks on an approval.</summary>
    internal const string ApprovalPushToggleKey = "Taskboard:Chat:Notify:Approval:Push";

    public async Task ApprovalAskedAsync(
        ChatRun run, string approvalId, string toolName, CancellationToken ct = default)
    {
        if (!configuration.GetEffectiveBool(ApprovalPushToggleKey))
        {
            return;
        }

        var conversation = await conversations
            .GetAsync(run.ConversationId, ct)
            .ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(new
        {
            runId = run.Id.Value,
            conversationId = run.ConversationId.Value,
            approvalId,
            toolName,
            title = conversation?.Title,
            status = "waiting-approval",
            url = $"/ai-chat?c={Uri.EscapeDataString(run.ConversationId.Value)}",
        });

        await SendToAllAsync(payload, ct).ConfigureAwait(false);
    }

    public async Task RunCompletedAsync(ChatRun run, CancellationToken ct = default)
    {
        if (!configuration.GetEffectiveBool(PushToggleKey))
        {
            return;
        }

        var conversation = await conversations
            .GetAsync(run.ConversationId, ct)
            .ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(new
        {
            runId = run.Id.Value,
            conversationId = run.ConversationId.Value,
            title = conversation?.Title,
            status = run.Status.Value,
            error = run.Error,
            url = $"/ai-chat?c={Uri.EscapeDataString(run.ConversationId.Value)}",
        });

        await SendToAllAsync(payload, ct).ConfigureAwait(false);
    }

    private async Task SendToAllAsync(string payload, CancellationToken ct)
    {
        var subs = await subscriptions.ListAsync(ct).ConfigureAwait(false);
        if (subs.Count == 0)
        {
            return;
        }

        foreach (var sub in subs)
        {
            var result = await sender
                .SendAsync(sub.Endpoint, sub.P256dh, sub.Auth, payload, ct)
                .ConfigureAwait(false);
            if (result.Sent)
            {
                continue;
            }

            if (result.HttpStatus is 404 or 410)
            {
                logger.LogInformation("pruning dead push subscription {Id} ({Status})", sub.Id, result.HttpStatus);
                await subscriptions.DeleteAsync(sub, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                logger.LogWarning(
                    "web push delivery failed for subscription {Id}: {Error} ({Status})",
                    sub.Id, result.Error, result.HttpStatus);
            }
        }

        await subscriptions.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
