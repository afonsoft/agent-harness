namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// FTS5-backed full-text index over chat messages (SPEC-20261005-chat-jobs-schedule-search
/// RF-007/RF-008). Synchronous-in-transaction sync on insert — callers index
/// right after persisting a message; failures degrade search, never the write.
/// Implemented over raw SQLite in the EF layer (Contracts sees no EF).
/// </summary>
public interface IChatMessageSearchIndex
{
    /// <summary>Indexes one message row (idempotent by messageId — replace on re-index).</summary>
    Task IndexAsync(
        string messageId, string conversationId, string content,
        CancellationToken cancellationToken = default);

    /// <summary>bm25-ranked hits, best first; snippets carry <c>&lt;mark&gt;</c>s.</summary>
    Task<IReadOnlyList<ChatSearchHitDto>> SearchAsync(
        string query, string? conversationId, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Drops every row of a conversation (delete path / index rebuild).</summary>
    Task RemoveConversationAsync(string conversationId, CancellationToken cancellationToken = default);
}
