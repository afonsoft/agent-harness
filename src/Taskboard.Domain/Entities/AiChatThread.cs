using Taskboard;
using Taskboard.Agents;
using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities;

public sealed class AiChatThread : AggregateRoot<AiChatThreadId>
{
    private readonly List<AiChatRun> _runs = new();
    private readonly List<AiChatEvent> _events = new();

    public string Title { get; private set; } = default!;
    public ModelRef Model { get; private set; } = default!;
    public string ReasoningEffort { get; private set; } = default!;
    public Sandbox Sandbox { get; private set; } = default!;
    public AiChatThreadStatus Status { get; private set; } = default!;
    public string Mode { get; private set; } = "assistant";
    public AgentType? AgentType { get; private set; }
    public string? WorkspacePath { get; private set; }
    public string? RepositoryFullName { get; private set; }
    /// <summary>SPEC-20260921-ai-code-thread-config RF-006: tier escolhido na criação (Lite|Normal|Ultra), quando aplicável.</summary>
    public string? ModelTier { get; private set; }
    /// <summary>SPEC-20260921-ai-code-thread-config RF-006: catálogo que serviu o modelo efetivo (acp|probe|curated|custom).</summary>
    public string? ModelSource { get; private set; }
    /// <summary>SPEC-20260928-ai-code-generic-cli RF-003: "acp" (structured session) | "pty" (raw terminal). Immutable.</summary>
    public string Transport { get; private set; } = "acp";
    /// <summary>SPEC-20260928-ai-code-generic-cli RF-004: container name when the CLI runs via docker exec; null → host.</summary>
    public string? ContainerContext { get; private set; }
    /// <summary>SPEC-20260928-ai-code-generic-cli RF-002: custom CLI definition id (custom-*) — exclusive with <see cref="AgentType"/>.</summary>
    public string? AgentCliId { get; private set; }
    public IReadOnlyCollection<AiChatRun> Runs => _runs.AsReadOnly();
    public IReadOnlyCollection<AiChatEvent> Events => _events.AsReadOnly();
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private AiChatThread()
    {
    }

    private AiChatThread(
        AiChatThreadId id,
        string title,
        ModelRef model,
        string reasoningEffort,
        Sandbox sandbox,
        DateTime now,
        string mode = "assistant",
        AgentType? agentType = null,
        string? workspacePath = null,
        string? repositoryFullName = null)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException(TaskboardDomainErrorCodes.EmptyTitle, "Thread title cannot be empty.");
        }

        Title = title;
        Model = model;
        ReasoningEffort = reasoningEffort;
        Sandbox = sandbox;
        Status = AiChatThreadStatus.Idle;
        Mode = string.IsNullOrWhiteSpace(mode) ? "assistant" : mode;
        AgentType = agentType;
        WorkspacePath = workspacePath;
        RepositoryFullName = repositoryFullName;
        CreatedAt = UpdatedAt = now;
    }

    public static AiChatThread Create(
        AiChatThreadId id,
        string title,
        ModelRef model,
        string reasoningEffort,
        Sandbox sandbox,
        DateTime? now = null,
        AgentType? agentType = null,
        string? repositoryFullName = null)
        => new(
            id,
            title,
            model,
            reasoningEffort,
            sandbox,
            now ?? DateTime.UtcNow,
            agentType: agentType,
            repositoryFullName: repositoryFullName);

    /// <summary>
    /// SPEC-20260928: <paramref name="agentType"/> is nullable — terminal
    /// threads bound to a custom CLI definition carry <c>AgentCliId</c>
    /// instead of a builtin <see cref="Taskboard.Agents.AgentType"/>.
    /// </summary>
    public static AiChatThread CreateAgentThread(
        AiChatThreadId id,
        string title,
        ModelRef model,
        string reasoningEffort,
        Sandbox sandbox,
        AgentType? agentType,
        string? workspacePath = null,
        string? repositoryFullName = null,
        DateTime? now = null)
        => new(
            id,
            title,
            model,
            reasoningEffort,
            sandbox,
            now ?? DateTime.UtcNow,
            mode: "agent",
            agentType: agentType,
            workspacePath: workspacePath,
            repositoryFullName: repositoryFullName);

    public AiChatRun StartRun(DateTime? now = null)
    {
        var run = AiChatRun.Create(AiChatRunId.NewGuid(), Id, now ?? DateTime.UtcNow);
        _runs.Add(run);
        Status = AiChatThreadStatus.Running;
        UpdatedAt = now ?? DateTime.UtcNow;
        IncrementVersion();
        return run;
    }

    public void AddEvent(AiChatEvent chatEvent)
    {
        if (chatEvent is null)
        {
            throw new ArgumentNullException(nameof(chatEvent));
        }

        if (chatEvent.ThreadId != Id)
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Event does not belong to this thread.");
        }

        _events.Add(chatEvent);
        UpdatedAt = chatEvent.CreatedAt;
        IncrementVersion();
    }

    /// <summary>
    /// Binds an agent CLI (and optionally resets the model) — used by the
    /// legacy-thread migration: assistant threads created before agent binding
    /// was required get the first eligible agent on their next run
    /// (SPEC-20260921-ai-chat-cli-backend RF-005).
    /// </summary>
    public void BindAgent(AgentType agentType, ModelRef? model = null, DateTime? now = null)
    {
        AgentType = agentType;
        if (model is not null)
        {
            Model = model;
        }
        UpdatedAt = now ?? DateTime.UtcNow;
        IncrementVersion();
    }

    /// <summary>
    /// SPEC-20260928-ai-code-generic-cli: sets the CLI binding extras — transport
    /// (<c>"acp"</c>|<c>"pty"</c>), docker container context and custom CLI id.
    /// Called once at creation; transport is immutable afterwards.
    /// </summary>
    public void ConfigureCli(string? transport, string? containerContext, string? agentCliId, DateTime? now = null)
    {
        if (!string.IsNullOrWhiteSpace(transport))
        {
            Transport = string.Equals(transport, "pty", StringComparison.OrdinalIgnoreCase) ? "pty" : "acp";
        }

        ContainerContext = string.IsNullOrWhiteSpace(containerContext)
            || string.Equals(containerContext, "host", StringComparison.OrdinalIgnoreCase)
            ? null
            : containerContext.Trim();
        AgentCliId = string.IsNullOrWhiteSpace(agentCliId) ? null : agentCliId.Trim();
        UpdatedAt = now ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Records which catalog served the effective model choice
    /// (SPEC-20260921-ai-code-thread-config RF-006) — pure audit metadata,
    /// set once at creation.
    /// </summary>
    public void SetModelChoice(string? modelTier, string? modelSource)
    {
        ModelTier = modelTier;
        ModelSource = modelSource;
    }

    public void SetStatus(AiChatThreadStatus status, DateTime? now = null)
    {
        Status = status;
        UpdatedAt = now ?? DateTime.UtcNow;
        IncrementVersion();
    }

    public void UpdateTitle(string title, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException(TaskboardDomainErrorCodes.EmptyTitle, "Thread title cannot be empty.");
        }

        Title = title;
        UpdatedAt = now ?? DateTime.UtcNow;
        IncrementVersion();
    }
}
