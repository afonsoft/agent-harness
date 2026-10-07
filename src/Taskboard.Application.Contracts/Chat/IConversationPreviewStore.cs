namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261015-chat-preview-panel RF-002: write access to a conversation's
/// pinned preview URL. Kept in Contracts so integrations-side tools (no
/// Domain reference) can announce an app without reaching the EF layer.
/// The caller passes an already-normalized <c>/preview/{port}/{path}</c>.
/// </summary>
public interface IConversationPreviewStore
{
    /// <summary>Sets/clears the preview URL; false when the conversation is gone.</summary>
    Task<bool> SetPreviewUrlAsync(
        string conversationId, string? normalizedPreviewUrl, CancellationToken cancellationToken);
}
