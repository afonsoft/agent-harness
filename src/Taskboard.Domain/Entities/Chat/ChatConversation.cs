using Taskboard;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>
/// A provider chat conversation (SPEC-20260929-ai-code-provider-chat RF-004).
/// Provider name is denormalized so history stays readable after the provider
/// is deleted; sending fails with a clear error in that case (RF-001).
/// </summary>
public sealed class ChatConversation : AggregateRoot<ChatConversationId>
{
    private readonly List<ChatMessage> _messages = new();

    public Guid ProviderId { get; private set; }
    public string ProviderName { get; private set; } = default!;
    public string Model { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public IReadOnlyCollection<ChatMessage> Messages => _messages.AsReadOnly();
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // Agent-chat binding (SPEC-20261003-ai-code-agent-chat) — null on plain
    // provider-chat conversations.
    public string? AgentCli { get; private set; }
    public string? RepositoryFullName { get; private set; }
    public string? WorkspacePath { get; private set; }
    public string? AgentModel { get; private set; }

    private ChatConversation()
    {
    }

    private ChatConversation(
        ChatConversationId id, Guid providerId, string providerName, string model, string title, DateTime now)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException(TaskboardDomainErrorCodes.EmptyTitle, "Conversation title cannot be empty.");
        }

        ProviderId = providerId;
        ProviderName = providerName;
        Model = model;
        Title = title;
        CreatedAt = UpdatedAt = now;
    }

    public static ChatConversation Create(
        ChatConversationId id, Guid providerId, string providerName, string model,
        string? title = null, DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;
        return new ChatConversation(
            id, providerId, providerName, model,
            string.IsNullOrWhiteSpace(title) ? "Nova conversa" : title.Trim(), at);
    }

    /// <summary>Auto-derives the title from the first user message (~60 chars, RF-004).</summary>
    public void EnsureTitle(string firstUserMessage, DateTime now)
    {
        if (Title != "Nova conversa" || string.IsNullOrWhiteSpace(firstUserMessage))
        {
            return;
        }

        var trimmed = firstUserMessage.Trim();
        Title = trimmed.Length <= 60 ? trimmed : trimmed[..60];
        UpdatedAt = now;
    }

    public void Rename(string title, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException(TaskboardDomainErrorCodes.EmptyTitle, "Conversation title cannot be empty.");
        }

        Title = title.Trim();
        UpdatedAt = now;
    }

    public void ChangeModel(string model, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Model cannot be empty.");
        }

        Model = model;
        UpdatedAt = now;
    }

    /// <summary>
    /// Binds the conversation to an agent CLI/workspace — the delegated tools
    /// (run_agent, run_cli) default to these values.
    /// </summary>
    public void SetAgentContext(
        string? agentCli, string? repositoryFullName, string? workspacePath, string? agentModel)
    {
        AgentCli = NullIfBlank(agentCli);
        RepositoryFullName = NullIfBlank(repositoryFullName);
        WorkspacePath = NullIfBlank(workspacePath);
        AgentModel = NullIfBlank(agentModel);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Touch(DateTime now) => UpdatedAt = now;
}
