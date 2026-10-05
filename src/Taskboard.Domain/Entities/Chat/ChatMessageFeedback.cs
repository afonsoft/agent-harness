using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>Allowed feedback ratings (SPEC-20261005-chat-attachments-feedback RF-005).</summary>
public static class ChatFeedbackRatings
{
    public const string Positive = "positive";
    public const string Negative = "negative";

    public static bool IsValid(string rating) => rating is Positive or Negative;
}

/// <summary>Allowed feedback categories (RF-005) — empty string also allowed (= unset).</summary>
public static class ChatFeedbackCategories
{
    public const string Wrong = "wrong";
    public const string Unhelpful = "unhelpful";
    public const string Unsafe = "unsafe";
    public const string Slow = "slow";
    public const string Cost = "cost";
    public const string Other = "other";

    public static bool IsValid(string? category) =>
        category is null or Wrong or Unhelpful or Unsafe or Slow or Cost or Other;
}

/// <summary>
/// Per-message 👍/👎 feedback (SPEC-20261005-chat-attachments-feedback
/// RF-005/RNF-003). At most one row per message (unique index); the row is
/// log-only — it never enters the provider wire. <see cref="Version"/> is the
/// CAS token for the upsert endpoint.
/// </summary>
public sealed class ChatMessageFeedback : AggregateRoot<ChatMessageFeedbackId>
{
    public const int MaxNoteLength = 1000;

    public ChatMessageId MessageId { get; private set; } = default!;
    public ChatConversationId ConversationId { get; private set; } = default!;
    public string Rating { get; private set; } = default!;
    public string? Category { get; private set; }
    public string? Note { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private ChatMessageFeedback()
    {
    }

    private ChatMessageFeedback(
        ChatMessageFeedbackId id, ChatMessageId messageId, ChatConversationId conversationId,
        string rating, string? category, string? note, DateTime now)
        : base(id)
    {
        MessageId = messageId;
        ConversationId = conversationId;
        CreatedAt = UpdatedAt = now;
        Apply(rating, category, note, now, bump: false);
    }

    public static ChatMessageFeedback Create(
        ChatMessageFeedbackId id, ChatMessageId messageId, ChatConversationId conversationId,
        string rating, string? category, string? note, DateTime? now = null) =>
        new(id, messageId, conversationId, rating, category, note, now ?? DateTime.UtcNow);

    /// <summary>Upserts rating/category/note and bumps the CAS <see cref="Version"/>.</summary>
    public void Rate(string rating, string? category, string? note, DateTime? now = null) =>
        Apply(rating, category, note, now ?? DateTime.UtcNow, bump: true);

    private void Apply(string rating, string? category, string? note, DateTime now, bool bump)
    {
        if (!ChatFeedbackRatings.IsValid(rating))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, $"Unknown feedback rating '{rating}'.");
        }

        if (!ChatFeedbackCategories.IsValid(category))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, $"Unknown feedback category '{category}'.");
        }

        if (note is { Length: > MaxNoteLength })
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Feedback note exceeds {MaxNoteLength} chars.");
        }

        Rating = rating;
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        UpdatedAt = now;
        if (bump)
        {
            IncrementVersion();
        }
    }
}
