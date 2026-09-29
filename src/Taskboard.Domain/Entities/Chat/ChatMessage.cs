using Taskboard;
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
    public int? TokensIn { get; private set; }
    public int? TokensOut { get; private set; }
    public string? Model { get; private set; }
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
