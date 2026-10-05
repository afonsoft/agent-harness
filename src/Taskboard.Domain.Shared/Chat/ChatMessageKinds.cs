namespace Taskboard.Chat;

/// <summary>
/// SPEC-20261005-chat-context-management RF-004: <c>ChatMessage.Kind</c>
/// discriminator — "normal" rows vs persisted compaction summaries
/// ("summary" rows carry SupersedesUntilMessageId and are re-emitted on the
/// wire in place of the superseded prefix).
/// </summary>
public static class ChatMessageKinds
{
    public const string Normal = "normal";
    public const string Summary = "summary";
}
