using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Application.Mapping;
using Taskboard.Domain.Entities;
using Taskboard.Dtos;
using Taskboard.Repositories;
using Taskboard.Requests;
using Taskboard.ValueObjects;

namespace Taskboard.Application.AiChat;

public sealed class AiChatService
{
    private readonly IRepository<AiChatThread> _threadRepo;
    private readonly IRepository<AiChatRun> _runRepo;
    private readonly IRepository<AiChatEvent> _eventRepo;
    private readonly ILLMProvider _llmProvider;
    private readonly IThreadEventStreamService _threadEvents;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IAgentEligibilityService _eligibility;
    private readonly ICliChatRunner _cliChatRunner;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiChatService> _logger;

    public AiChatService(
        IRepository<AiChatThread> threadRepo,
        IRepository<AiChatRun> runRepo,
        IRepository<AiChatEvent> eventRepo,
        ILLMProvider llmProvider,
        IThreadEventStreamService threadEvents,
        IServiceScopeFactory serviceScopeFactory,
        IAgentEligibilityService eligibility,
        ICliChatRunner cliChatRunner,
        IConfiguration configuration,
        ILogger<AiChatService> logger)
    {
        _threadRepo = threadRepo;
        _runRepo = runRepo;
        _eventRepo = eventRepo;
        _llmProvider = llmProvider;
        _threadEvents = threadEvents;
        _serviceScopeFactory = serviceScopeFactory;
        _eligibility = eligibility;
        _cliChatRunner = cliChatRunner;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AiChatThreadDto> CreateThreadAsync(
        CreateAiChatThreadRequest request,
        Actor actor,
        CancellationToken ct = default)
    {
        // SPEC-20260921-ai-chat-cli-backend RF-002: every thread is backed by an
        // eligible agent CLI — there is no direct-LLM provider in the server.
        if (string.IsNullOrWhiteSpace(request.AgentType) ||
            !Enum.TryParse<AgentType>(request.AgentType, true, out var agentType))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"Invalid agent type '{request.AgentType}'.");
        }

        var eligible = await _eligibility.GetEligibleTypesAsync(ct);
        if (!eligible.Contains(agentType))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.AgentNotEligible,
                $"Agent '{agentType}' is not eligible — the CLI must be installed, authenticated and enabled.");
        }

        var model = string.IsNullOrWhiteSpace(request.Model) ? ModelRef.From("default") : ModelRef.From(request.Model);
        var reasoningEffort = string.IsNullOrWhiteSpace(request.ReasoningEffort) ? "medium" : request.ReasoningEffort;
        var sandbox = string.IsNullOrWhiteSpace(request.Sandbox) ? Sandbox.WorkspaceWrite : Sandbox.From(request.Sandbox);

        AiChatThread thread;
        if (string.Equals(request.Mode, "agent", StringComparison.OrdinalIgnoreCase))
        {
            thread = AiChatThread.CreateAgentThread(
                AiChatThreadId.NewGuid(),
                request.Title,
                model,
                reasoningEffort,
                sandbox,
                agentType,
                request.WorkspacePath,
                request.RepositoryFullName);
        }
        else
        {
            thread = AiChatThread.Create(
                AiChatThreadId.NewGuid(),
                request.Title,
                model,
                reasoningEffort,
                sandbox,
                agentType: agentType);
        }

        await _threadRepo.AddAsync(thread, ct);
        await _threadRepo.SaveChangesAsync(ct);

        return thread.ToDto();
    }

    public async Task<AiChatThreadDto?> GetThreadAsync(AiChatThreadId id, CancellationToken ct = default)
    {
        var thread = await _threadRepo.GetAsync(id, ct);
        return thread?.ToDto();
    }

    public async Task<IReadOnlyList<AiChatThreadDto>> ListThreadsAsync(CancellationToken ct = default)
    {
        var threads = await _threadRepo.ListAsync(ct);
        return threads.Select(t => t.ToDto()).ToList().AsReadOnly();
    }

    public async Task<bool> DeleteThreadAsync(AiChatThreadId id, CancellationToken ct = default)
    {
        var thread = await _threadRepo.GetAsync(id, ct);
        if (thread is null)
        {
            return false;
        }

        await _threadRepo.DeleteAsync(thread, ct);
        await _threadRepo.SaveChangesAsync(ct);
        return true;
    }

    public async Task<AiChatRunDto> StartRunAsync(
        AiChatThreadId threadId,
        Actor actor,
        CancellationToken ct = default)
    {
        var thread = await _threadRepo.GetAsync(threadId, ct);
        if (thread is null)
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"Thread '{threadId.Value}' not found.");
        }

        // SPEC-20260921-ai-chat-cli-backend RF-005: legacy assistant threads
        // without a bound agent auto-migrate to the first eligible CLI.
        if (thread.Mode == "assistant" && thread.AgentType is null)
        {
            var eligible = await _eligibility.GetEligibleTypesAsync(ct);
            var pick = eligible.OrderBy(t => t.ToString(), StringComparer.Ordinal).FirstOrDefault();
            if (eligible.Count > 0)
            {
                // Keep the stored model only when it is a real model of the
                // bound CLI; legacy provider names (gpt-4o, …) reset to default.
                var model = AgentCliModels.Catalog(pick).Contains(thread.Model.Value, StringComparer.Ordinal)
                    ? thread.Model
                    : ModelRef.From("default");
                thread.BindAgent(pick, model);
                await _threadRepo.UpdateAsync(thread, ct);
            }
        }

        var run = thread.StartRun();
        await _runRepo.AddAsync(run, ct);
        await _threadRepo.SaveChangesAsync(ct);

        await _threadEvents.PublishAsync(
            threadId.Value,
            new ServerSentEvent("ai_chat.run", run.ToDto()),
            ct);

        // SPEC-20260918-ai-chat-threads: the run must outlive this request's DI
        // scope — the repositories above are scoped and their DbContext is
        // disposed when the HTTP request ends. A fresh scope keeps the
        // background execution alive (and CancellationToken.None keeps it from
        // dying with the request token).
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<AiChatService>();
            await service.ExecuteRunAsync(thread.Id, run.Id, CancellationToken.None);
        });

        return run.ToDto();
    }

    private async System.Threading.Tasks.Task ExecuteRunAsync(AiChatThreadId threadId, AiChatRunId runId, CancellationToken ct)
    {
        try
        {
            var run = await _runRepo.GetAsync(runId, ct);
            var thread = await _threadRepo.GetAsync(threadId, ct);

            if (run is null || thread is null) return;

            // Get conversation history
            var events = await _eventRepo.Query
                .Where(e => e.ThreadId == threadId)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(ct);

            // SPEC-20260921-ai-chat-cli-backend: assistant runs go through the
            // bound agent CLI (one-shot, transcript as prompt). The mock
            // provider is kept only behind Taskboard:AiChat:MockProvider=true
            // for dev/tests; a thread without a bound agent fails loudly.
            var mockEnabled = bool.TryParse(_configuration["Taskboard:AiChat:MockProvider"], out var mock) && mock;
            if (!mockEnabled && thread.AgentType is null)
            {
                await EmitCliEventAsync(
                    threadId,
                    "No eligible agent CLI is bound to this thread — authenticate one in Agents or Settings and send again.",
                    AiChatEventKind.Error);
                run.Fail(-1);
                thread.SetStatus(AiChatThreadStatus.Failed);
            }
            else if (!mockEnabled && thread.AgentType is not null)
            {
                var prompt = AgentThreadPromptBuilder.BuildAssistantPrompt(
                    thread.Title,
                    events.Select(e => e.ToDto()).ToList());
                var modelName = string.Equals(thread.Model.Value, "default", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : thread.Model.Value;
                var progress = new Progress<AgentLogMessage>(log =>
                    _ = EmitCliEventAsync(
                        threadId,
                        log.Content,
                        log.Stream == AgentLogStream.StdErr ? AiChatEventKind.Error : AiChatEventKind.Message));

                var result = await _cliChatRunner.RunAsync(
                    thread.Id.Value, thread.AgentType.GetValueOrDefault(), modelName, prompt, progress, ct);

                if (result.IsSuccess)
                {
                    run.Complete((int)(result.Usage?.TotalTokens ?? 0));
                    thread.SetStatus(AiChatThreadStatus.Idle);
                }
                else
                {
                    run.Fail(result.ExitCode);
                    thread.SetStatus(AiChatThreadStatus.Failed);
                    await EmitCliEventAsync(threadId, $"Agent CLI exited with code {result.ExitCode}.", AiChatEventKind.Error);
                }
            }
            else
            {
                var messages = new List<LLMMessage>
                {
                    new("system", $"You are an AI assistant in sandbox mode: {thread.Sandbox.Value}.")
                };

                foreach (var ev in events)
                {
                    messages.Add(new LLMMessage(
                        ev.Role.Value switch
                        {
                            "user" => "user",
                            "assistant" => "assistant",
                            _ => "system"
                        },
                        ev.Content));
                }

                await foreach (var chunk in _llmProvider.StreamAsync(messages, cancellationToken: ct))
                {
                    if (chunk.IsComplete)
                    {
                        run.Complete(chunk.Usage?.TotalTokens ?? 0);
                        break;
                    }

                    if (!string.IsNullOrEmpty(chunk.ContentDelta))
                    {
                        var chatEvent = AiChatEvent.Create(
                            AiChatEventId.NewGuid(),
                            threadId,
                            AiChatEventRole.Assistant,
                            chunk.ContentDelta);

                        thread.AddEvent(chatEvent);
                        await _eventRepo.AddAsync(chatEvent, ct);

                        await _threadEvents.PublishAsync(
                            threadId.Value,
                            new ServerSentEvent("ai_chat.event", chatEvent.ToDto()),
                            ct);
                    }
                }

                thread.SetStatus(AiChatThreadStatus.Idle);
            }
            await _runRepo.UpdateAsync(run, ct);
            await _threadRepo.UpdateAsync(thread, ct);
            await _threadRepo.SaveChangesAsync(ct);

            await _threadEvents.PublishAsync(
                threadId.Value,
                new ServerSentEvent("ai_chat.run", run.ToDto()),
                CancellationToken.None);
        }
        catch (Exception)
        {
            var run = await _runRepo.GetAsync(runId, ct);
            var thread = await _threadRepo.GetAsync(threadId, ct);

            if (run != null)
            {
                run.Fail(-1);
                await _runRepo.UpdateAsync(run, ct);
            }

            if (thread != null)
            {
                thread.SetStatus(AiChatThreadStatus.Failed);
                await _threadRepo.UpdateAsync(thread, ct);
            }

            await _threadRepo.SaveChangesAsync(ct);

            if (run != null)
            {
                await _threadEvents.PublishAsync(
                    threadId.Value,
                    new ServerSentEvent("ai_chat.run", run.ToDto()),
                    CancellationToken.None);
            }
        }
    }

    // Persists a CLI-produced line as a thread event in a fresh scope — the
    // progress callback fires on the process-output thread, so the scoped
    // DbContext of this run must not be touched there (same pattern as
    // AgentSessionManager.ExecuteOneShotFallbackAsync).
    private async Task EmitCliEventAsync(AiChatThreadId threadId, string content, AiChatEventKind kind)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var eventRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatEvent>>();
            var evt = AiChatEvent.CreateTyped(
                AiChatEventId.NewGuid(),
                threadId,
                AiChatEventRole.Assistant,
                content,
                kind);
            await eventRepo.AddAsync(evt);
            await eventRepo.SaveChangesAsync();
            await _threadEvents.PublishAsync(
                threadId.Value,
                new ServerSentEvent("ai_chat.event", evt.ToDto()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist CLI chat event for thread '{ThreadId}'.", threadId.Value);
        }
    }

    public async Task<AiChatEventDto> AddEventAsync(
        AiChatThreadId threadId,
        AddAiChatEventRequest request,
        Actor actor,
        CancellationToken ct = default)
    {
        var thread = await _threadRepo.GetAsync(threadId, ct);
        if (thread is null)
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"Thread '{threadId.Value}' not found.");
        }

        var role = AiChatEventRole.From(request.Role);
        var kind = !string.IsNullOrWhiteSpace(request.Kind)
            ? AiChatEventKind.From(request.Kind)
            : AiChatEventKind.Message;

        var chatEvent = AiChatEvent.CreateTyped(
            AiChatEventId.NewGuid(),
            threadId,
            role,
            request.Content,
            kind,
            request.PayloadJson);

        thread.AddEvent(chatEvent);
        await _eventRepo.AddAsync(chatEvent, ct);
        await _threadRepo.SaveChangesAsync(ct);

        await _threadEvents.PublishAsync(
            threadId.Value,
            new ServerSentEvent("ai_chat.event", chatEvent.ToDto()),
            ct);

        return chatEvent.ToDto();
    }

    public async Task<IReadOnlyList<AiChatEventDto>> GetEventsAsync(
        AiChatThreadId threadId,
        CancellationToken ct = default)
    {
        var events = await _eventRepo.Query
            .Where(e => e.ThreadId == threadId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        return events.Select(e => e.ToDto()).ToList().AsReadOnly();
    }
}