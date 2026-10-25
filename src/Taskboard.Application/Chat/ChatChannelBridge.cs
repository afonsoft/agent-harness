using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261025 C1: routes inbound channel messages into conversations and
/// channel approve/reject actions into <see cref="ChatService.DecideApprovalAsync"/>.
/// First contact on a (channel, slack-channel, thread) creates the conversation
/// — default enabled provider, model from <c>Taskboard:Channels:&lt;channel&gt;:Model</c>
/// or the provider's first listed model — plus a <see cref="ChatChannelBinding"/>.
/// </summary>
public sealed class ChatChannelBridge(
    IRepository<ChatChannelBinding> bindings,
    IRepository<ChatProvider> providers,
    ChatService chat,
    IConfiguration configuration,
    ILogger<ChatChannelBridge> logger) : IChatChannelBridge
{
    public async Task<string> InboundMessageAsync(
        ChatChannelInboundMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
        {
            throw new ChatValidationException("Channel message text is empty.");
        }

        var binding = await bindings.Query
            .FirstOrDefaultAsync(
                b => b.Channel == message.Channel
                    && b.ExternalChannelId == message.ExternalChannelId
                    && b.ExternalThreadId == message.ExternalThreadId,
                cancellationToken)
            .ConfigureAwait(false);

        string conversationId;
        if (binding is not null)
        {
            conversationId = binding.ConversationId.Value;
        }
        else
        {
            conversationId = await CreateBoundConversationAsync(message, cancellationToken)
                .ConfigureAwait(false);
        }

        await chat.EnqueueMessageAsync(conversationId, message.Text, cancellationToken)
            .ConfigureAwait(false);
        return conversationId;
    }

    public Task<ChatApprovalDto?> DecideApprovalAsync(
        string approvalId, string outcome, string channel, string? actor,
        string? reason, CancellationToken cancellationToken = default) =>
        chat.DecideApprovalAsync(
            approvalId,
            new DecideChatApprovalRequest(
                outcome, Reason: reason, Channel: channel, Actor: actor),
            cancellationToken);

    private async Task<string> CreateBoundConversationAsync(
        ChatChannelInboundMessage message, CancellationToken ct)
    {
        var provider = (await providers.Query
                .Where(p => p.Enabled)
                .OrderBy(p => p.Name)
                .ToListAsync(ct).ConfigureAwait(false))
            .FirstOrDefault()
            ?? throw new ChatValidationException("No enabled chat provider for channel-bound conversation.");

        var model = configuration[$"Taskboard:Channels:{message.Channel}:Model"];
        if (string.IsNullOrWhiteSpace(model))
        {
            try
            {
                model = (await chat.ListModelsAsync(provider.Id, ct).ConfigureAwait(false))
                    .Models.FirstOrDefault();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "channel bridge: model probe failed for provider {Provider}", provider.Name);
            }
        }

        var title = message.Text.Trim();
        title = title.Length <= 60 ? title : $"{title[..57]}…";
        var conversation = await chat.CreateConversationAsync(
            new CreateChatConversationRequest(provider.Id, model ?? string.Empty, title), ct)
            .ConfigureAwait(false);

        var binding = ChatChannelBinding.Create(
            ChatChannelBindingId.NewGuid(), message.Channel, message.ExternalChannelId,
            message.ExternalThreadId, message.ExternalUserId,
            ChatConversationId.From(conversation.Id));
        await bindings.AddAsync(binding, ct).ConfigureAwait(false);
        await bindings.SaveChangesAsync(ct).ConfigureAwait(false);
        return conversation.Id;
    }
}
