using Taskboard;
using Taskboard.Chat;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>
/// One message of a provider conversation (SPEC-20260929-ai-code-provider-chat
/// RF-004). Assistant messages may carry tool calls (JSON array of
/// <c>{id, name, arguments}</c>); tool messages carry the confined result and
/// the refusal flag from the security gateway (RF-006).
/// </summary>
public sealed class ChatMessage : Entity<ChatMessageId>
{
    public ChatConversationId ConversationId { get; private set; } = default!;
    public ChatMessageRole Role { get; private set; } = default!;
    public string Content { get; private set; } = default!;
    /// <summary>Assistant-only: JSON array of OpenAI tool calls emitted by the model.</summary>
    public string? ToolCallsJson { get; private set; }
    /// <summary>Tool-only: the OpenAI tool_call id this result answers.</summary>
    public string? ToolCallId { get; private set; }
    public string? ToolName { get; private set; }
    /// <summary>Tool-only: true when the security gateway refused the call (RF-006).</summary>
    public bool Refused { get; private set; }
    /// <summary>Relative path of a generated image under the data dir (RF-009).</summary>
    public string? ImagePath { get; private set; }
    /// <summary>
    /// SPEC-20261005-chat-fork-steering RF-001: back-pointer to the source
    /// message this row was copied from when the conversation was forked.
    /// Null on originals.
    /// </summary>
    public string? ForkedFromMessageId { get; private set; }
    public int? TokensIn { get; private set; }
    public int? TokensOut { get; private set; }
    public string? Model { get; private set; }
    /// <summary>
    /// SPEC-20261005-chat-context-management RF-004: "normal" | "summary" —
    /// summary rows persist a compaction result and supersede older rows on
    /// the provider wire (history itself is never rewritten).
    /// </summary>
    public string Kind { get; private set; } = ChatMessageKinds.Normal;
    /// <summary>Id of the last stored message this summary supersedes on the wire.</summary>
    public string? SupersedesUntilMessageId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ChatMessage()
    {
    }

    private ChatMessage(
        ChatMessageId id,
        ChatConversationId conversationId,
        ChatMessageRole role,
        string content,
        DateTime createdAt,
        string? toolCallsJson = null,
        string? toolCallId = null,
        string? toolName = null,
        bool refused = false,
        string? imagePath = null,
        int? tokensIn = null,
        int? tokensOut = null,
        string? model = null)
        : base(id)
    {
        ConversationId = conversationId;
        Role = role;
        Content = content;
        CreatedAt = createdAt;
        ToolCallsJson = toolCallsJson;
        ToolCallId = toolCallId;
        ToolName = toolName;
        Refused = refused;
        ImagePath = imagePath;
        TokensIn = tokensIn;
        TokensOut = tokensOut;
        Model = model;
    }

    public static ChatMessage CreateUser(
        ChatConversationId conversationId, string content, DateTime? now = null) =>
        new(ChatMessageId.NewGuid(), conversationId, ChatMessageRole.User, content, now ?? DateTime.UtcNow);

    public static ChatMessage CreateAssistant(
        ChatConversationId conversationId, string content, string? toolCallsJson = null,
        int? tokensIn = null, int? tokensOut = null, string? model = null, DateTime? now = null) =>
        new(ChatMessageId.NewGuid(), conversationId, ChatMessageRole.Assistant, content,
            now ?? DateTime.UtcNow, toolCallsJson, model: model, tokensIn: tokensIn, tokensOut: tokensOut);

    public static ChatMessage CreateTool(
        ChatConversationId conversationId, string toolCallId, string toolName,
        string resultJson, bool refused = false, DateTime? now = null) =>
        new(ChatMessageId.NewGuid(), conversationId, ChatMessageRole.Tool, resultJson,
            now ?? DateTime.UtcNow, toolCallId: toolCallId, toolName: toolName, refused: refused);

    /// <summary>
    /// SPEC-20261005-chat-tool-approval RNF-003: audit note in the transcript —
    /// approval decisions and permission-preset changes. Role "system" is a UI
    /// row only; the provider wire transcript skips it.
    /// </summary>
    public static ChatMessage CreateSystemNote(
        ChatConversationId conversationId, string content, DateTime? now = null) =>
        new(ChatMessageId.NewGuid(), conversationId, ChatMessageRole.System, content, now ?? DateTime.UtcNow);

    /// <summary>
    /// SPEC-20261005-chat-context-management RF-004: persisted compaction
    /// summary — supersedes stored messages up to
    /// <paramref name="supersedesUntilMessageId"/> on the wire while the full
    /// history stays in the table for the UI (RNF-001).
    /// </summary>
    public static ChatMessage CreateSummary(
        ChatConversationId conversationId, string content,
        string supersedesUntilMessageId, DateTime? now = null)
    {
        var message = new ChatMessage(
            ChatMessageId.NewGuid(), conversationId, ChatMessageRole.System,
            content, now ?? DateTime.UtcNow)
        {
            Kind = ChatMessageKinds.Summary,
            SupersedesUntilMessageId = supersedesUntilMessageId,
        };
        return message;
    }

    /// <summary>
    /// SPEC-20261005-chat-fork-steering RF-006/008: a user message claimed
    /// from the steer inbox — same wire shape as a normal user turn, marked
    /// <c>Kind="steer"</c> so the transcript shows it entered mid-turn.
    /// </summary>
    public static ChatMessage CreateSteer(
        ChatConversationId conversationId, string content, DateTime? now = null)
    {
        var message = new ChatMessage(
            ChatMessageId.NewGuid(), conversationId, ChatMessageRole.User,
            content, now ?? DateTime.UtcNow)
        {
            Kind = ChatMessageKinds.Steer,
        };
        return message;
    }

    /// <summary>
    /// SPEC-20261005-chat-fork-steering RF-001: clones <paramref name="source"/>
    /// into a forked conversation — caller mints <paramref name="newId"/>,
    /// back-pointer set, role, content, tool fields, kind and timestamps
    /// preserved. The caller remaps <see cref="SupersedesUntilMessageId"/>
    /// through the old→new id map.
    /// </summary>
    public static ChatMessage CreateForked(
        ChatMessageId newId, ChatConversationId conversationId, ChatMessage source,
        string? remappedSupersedesUntil = null)
    {
        var message = new ChatMessage(
            newId, conversationId, source.Role, source.Content,
            source.CreatedAt, source.ToolCallsJson, source.ToolCallId, source.ToolName,
            source.Refused, source.ImagePath, source.TokensIn, source.TokensOut, source.Model)
        {
            Kind = source.Kind,
            SupersedesUntilMessageId = remappedSupersedesUntil ?? source.SupersedesUntilMessageId,
            ForkedFromMessageId = source.Id.Value,
        };
        return message;
    }

    public void AttachImage(string imagePath, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Image path cannot be empty.");
        }

        ImagePath = imagePath;
        CreatedAt = now;
    }
}
