using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>
/// One changed/declared file of a finished run (SPEC-20261005-chat-attachments-
/// feedback RF-007). Rows are written once at run end from the file-tool edit
/// tracker, a git diff of the workspace, or an explicit <c>present</c> call —
/// they feed the deliverables card under the assistant message.
/// </summary>
public sealed class ChatRunDeliverable : Entity<ChatRunDeliverableId>
{
    public ChatRunId RunId { get; private set; } = default!;
    public ChatConversationId ConversationId { get; private set; } = default!;
    public string Path { get; private set; } = default!;

    /// <summary>Lines added; null when the source could not measure (e.g. binary).</summary>
    public int? AddedLines { get; private set; }

    /// <summary>Lines removed; null when unmeasured.</summary>
    public int? RemovedLines { get; private set; }

    public string Source { get; private set; } = default!;
    public string? Summary { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ChatRunDeliverable()
    {
    }

    private ChatRunDeliverable(
        ChatRunDeliverableId id, ChatRunId runId, ChatConversationId conversationId,
        string path, int? addedLines, int? removedLines, string source, string? summary, DateTime createdAt)
        : base(id)
    {
        RunId = runId;
        ConversationId = conversationId;
        Path = path;
        AddedLines = addedLines;
        RemovedLines = removedLines;
        Source = source;
        Summary = summary;
        CreatedAt = createdAt;
    }

    public static ChatRunDeliverable Create(
        ChatRunDeliverableId id, ChatRunId runId, ChatConversationId conversationId,
        string path, int? addedLines, int? removedLines, string source,
        string? summary = null, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Deliverable path cannot be empty.");
        }

        return new(
            id, runId, conversationId, path.Trim(), addedLines, removedLines, source,
            string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(), now ?? DateTime.UtcNow);
    }
}
