using Taskboard.Application.Contracts.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Application.Chat;

/// <summary>EF-backed <see cref="IConversationPreviewStore"/>.</summary>
public sealed class ConversationPreviewStore(IRepository<ChatConversation> conversations)
    : IConversationPreviewStore
{
    public async Task<bool> SetPreviewUrlAsync(
        string conversationId, string? normalizedPreviewUrl, CancellationToken cancellationToken)
    {
        var conversation = await conversations
            .GetAsync(ChatConversationId.From(conversationId), cancellationToken)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            return false;
        }

        conversation.SetPreviewUrl(normalizedPreviewUrl, DateTime.UtcNow);
        await conversations.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
