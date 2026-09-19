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
        DateTime? now = null)
        => new(id, title, model, reasoningEffort, sandbox, now ?? DateTime.UtcNow);

    public static AiChatThread CreateAgentThread(
        AiChatThreadId id,
        string title,
        ModelRef model,
        string reasoningEffort,
        Sandbox sandbox,
        AgentType agentType,
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
