using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskboard;
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

    public AiChatService(
        IRepository<AiChatThread> threadRepo,
        IRepository<AiChatRun> runRepo,
        IRepository<AiChatEvent> eventRepo,
        ILLMProvider llmProvider,
        IThreadEventStreamService threadEvents,
        IServiceScopeFactory serviceScopeFactory)
    {
        _threadRepo = threadRepo;
        _runRepo = runRepo;
        _eventRepo = eventRepo;
        _llmProvider = llmProvider;
        _threadEvents = threadEvents;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task<AiChatThreadDto> CreateThreadAsync(
        CreateAiChatThreadRequest request,
        Actor actor,
        CancellationToken ct = default)
    {
        var thread = AiChatThread.Create(
            AiChatThreadId.NewGuid(),
            request.Title,
            ModelRef.From(request.Model),
            request.ReasoningEffort,
            Sandbox.From(request.Sandbox));

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
        var chatEvent = AiChatEvent.Create(
            AiChatEventId.NewGuid(),
            threadId,
            role,
            request.Content);

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