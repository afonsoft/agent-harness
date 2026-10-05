using System.Text.Json;
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

    /// <summary>
    /// SPEC-20261005-chat-background-resume RF-006: soft-delete flag — archived
    /// conversations hide behind the "Arquivadas" filter, open read-only and
    /// block new sends until restored.
    /// </summary>
    public DateTime? ArchivedAt { get; private set; }

    // Agent-chat binding (SPEC-20261003-ai-code-agent-chat) — null on plain
    // provider-chat conversations.
    public string? AgentCli { get; private set; }
    public string? RepositoryFullName { get; private set; }
    public string? WorkspacePath { get; private set; }
    public string? AgentModel { get; private set; }

    /// <summary>
    /// SPEC-20261005-chat-tool-approval RF-006: per-conversation permission
    /// preset — <c>chat</c> (mutating calls refused), <c>ask</c> (default;
    /// confirm mutating calls), <c>full</c> (never ask). Editable mid-run;
    /// takes effect on the next tool call.
    /// </summary>
    public string PermissionPreset { get; private set; } = ChatPermissionPresets.Ask;

    /// <summary>
    /// JSON array of tool names the user always allows in this conversation
    /// (RF-004 <c>rememberTool</c>). Cleared when the preset changes.
    /// </summary>
    public string? AllowedToolsJson { get; private set; }

    /// <summary>
    /// SPEC-20261005-chat-plan-mode RF-001: <c>on</c> while the conversation
    /// plans (mutating tools denied, <c>exit_plan_mode</c> asks for review),
    /// <c>off</c> in normal execution. Editable mid-run.
    /// </summary>
    public string PlanMode { get; private set; } = ChatPlanModes.Off;

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

    /// <summary>
    /// SPEC-20261005-chat-tool-approval RF-006: switches the preset and clears
    /// the per-conversation allowed-list (open question #2 — the list is
    /// scoped to the preset it was granted under).
    /// </summary>
    public void SetPermissionPreset(string preset, DateTime now)
    {
        if (!ChatPermissionPresets.IsValid(preset))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Unknown permission preset '{preset}'.");
        }

        if (PermissionPreset == preset)
        {
            return;
        }

        PermissionPreset = preset;
        AllowedToolsJson = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// SPEC-20261005-chat-plan-mode RF-001: toggles plan mode — idempotent,
    /// returns true when the value actually flipped (the caller writes the
    /// audit note + cancels pending plan reviews on on→off).
    /// </summary>
    public bool SetPlanMode(bool on, DateTime now)
    {
        var target = on ? ChatPlanModes.On : ChatPlanModes.Off;
        if (PlanMode == target)
        {
            return false;
        }

        PlanMode = target;
        UpdatedAt = now;
        return true;
    }

    /// <summary>RF-004: adds a tool to the per-conversation allowed-list (idempotent).</summary>
    public void AllowTool(string toolName, DateTime now)
    {
        var list = new HashSet<string>(AllowedTools(), StringComparer.Ordinal);
        if (list.Add(toolName))
        {
            AllowedToolsJson = JsonSerializer.Serialize(list.Order());
            UpdatedAt = now;
        }
    }

    /// <summary>Per-conversation allowed-list (RF-004) — empty when unset/invalid.</summary>
    public IReadOnlySet<string> AllowedTools()
    {
        if (string.IsNullOrWhiteSpace(AllowedToolsJson))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(AllowedToolsJson);
            return list is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(list, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Archive is a view flag, not a state change — it does not reorder the
    /// history list (UpdatedAt stays; open question #2 in the spec).
    /// </summary>
    public void Archive(DateTime now) => ArchivedAt = now;

    /// <summary>Restores the conversation and surfaces it back on top of history.</summary>
    public void Unarchive(DateTime now)
    {
        ArchivedAt = null;
        UpdatedAt = now;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Touch(DateTime now) => UpdatedAt = now;
}
