namespace Taskboard.Application.Contracts.Chat;

/// <summary>Provider catalog DTO — the API key never leaves the server (RF-001).</summary>
public sealed record ChatProviderDto(
    Guid Id,
    string Name,
    string BaseUrl,
    bool Enabled,
    bool HasApiKey,
    string KeyHint,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ChatProviderUpsertRequest(
    string Name,
    string BaseUrl,
    string? ApiKey = null,
    bool? Enabled = null);

public sealed record ChatModelListDto(IReadOnlyList<string> Models, bool Cached);

/// <summary>
/// Agent-chat binding on a provider-chat conversation
/// (SPEC-20261003-ai-code-agent-chat): the CLI/repo/model picked in the Agent
/// mode bar. The assistant keeps answering; delegated tools (run_agent,
/// run_cli) default to this CLI and run inside this workspace.
/// </summary>
public sealed record ChatAgentContext(
    string? AgentCli = null,
    string? RepositoryFullName = null,
    string? WorkspacePath = null,
    string? AgentModel = null);

public sealed record ChatConversationDto(
    string Id,
    Guid ProviderId,
    string ProviderName,
    string Model,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? Preview,
    ChatAgentContext? Agent = null);

public sealed record ChatMessageDto(
    string Id,
    string Role,
    string Content,
    string? ToolCallsJson,
    string? ToolCallId,
    string? ToolName,
    bool Refused,
    string? ImagePath,
    string? ImageUrl,
    int? TokensIn,
    int? TokensOut,
    string? Model,
    DateTime CreatedAt);

public sealed record ChatConversationDetailDto(
    ChatConversationDto Conversation,
    IReadOnlyList<ChatMessageDto> Messages);

public sealed record CreateChatConversationRequest(
    Guid ProviderId,
    string Model,
    string? Title = null,
    ChatAgentContext? Agent = null);

public sealed record SendChatMessageRequest(string Content);

public sealed record PatchChatConversationRequest(
    string? Title = null,
    string? Model = null,
    ChatAgentContext? Agent = null);
