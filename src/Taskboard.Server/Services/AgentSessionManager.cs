using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Application.Mapping;
using Taskboard.Domain.Entities;
using Taskboard.Dtos;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Workspace;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Server.Services;

/// <summary>
/// Gerenciador de sessões de agentes interativos (Web CLI Agent) bound a threads.
/// </summary>
public sealed class AgentSessionManager : IAsyncDisposable
{
    private readonly AcpSessionClient _sessionClient;
    private readonly IAgentAcpClient _fallbackAcpClient;
    private readonly IEnumerable<IAgentAdapter> _adapters;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IThreadEventStreamService _threadEvents;
    private readonly PermissionGate _permissionGate;
    private readonly WorkspaceService _workspaceService;
    private readonly ILogger<AgentSessionManager> _logger;
    private readonly IAgentExecutionEventSink? _eventSink;
    private readonly CancellationTokenSource _reaperCts = new();

    // SPEC-20260921-ai-code-chat-ux RF-002: per-thread FIFO of queued prompts.
    private readonly ConcurrentDictionary<string, PromptQueue> _promptQueues = new();

    public AgentSessionManager(
        AcpSessionClient sessionClient,
        IAgentAcpClient fallbackAcpClient,
        IEnumerable<IAgentAdapter> adapters,
        IServiceScopeFactory scopeFactory,
        IThreadEventStreamService threadEvents,
        PermissionGate permissionGate,
        WorkspaceService workspaceService,
        ILogger<AgentSessionManager> logger,
        IAgentExecutionEventSink? eventSink = null)
    {
        _sessionClient = sessionClient;
        _fallbackAcpClient = fallbackAcpClient;
        _adapters = adapters;
        _scopeFactory = scopeFactory;
        _threadEvents = threadEvents;
        _permissionGate = permissionGate;
        _workspaceService = workspaceService;
        _logger = logger;
        _eventSink = eventSink;
    }

    public async Task<bool> EnsureSessionAsync(string threadId, CancellationToken cancellationToken = default)
    {
        if (_sessionClient.IsSessionActive(threadId))
        {
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var threadRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatThread>>();
        var thread = await threadRepo.GetAsync(AiChatThreadId.From(threadId), cancellationToken).ConfigureAwait(false);

        if (thread is null)
        {
            _logger.LogWarning("Thread '{ThreadId}' not found for agent session.", threadId);
            return false;
        }

        if (thread.Mode != "agent" || thread.AgentType is null)
        {
            _logger.LogWarning("Thread '{ThreadId}' is not in agent mode.", threadId);
            return false;
        }

        string workdir;
        if (!string.IsNullOrWhiteSpace(thread.WorkspacePath))
        {
            workdir = thread.WorkspacePath;
        }
        else if (!string.IsNullOrWhiteSpace(thread.RepositoryFullName))
        {
            workdir = _workspaceService.ResolveCardWorkdir(thread.RepositoryFullName, out _);
        }
        else
        {
            workdir = _workspaceService.EnsureRoot();
        }

        _sessionClient.RegisterEventListener(threadId, e => HandleSessionEvent(threadId, e));

        // Thread model reaches the session command; "default" = no flag, CLI decides.
        var sessionModel = string.Equals(thread.Model.Value, "default", StringComparison.OrdinalIgnoreCase)
            ? null
            : thread.Model.Value;

        var started = await _sessionClient.StartSessionAsync(
            threadId,
            thread.AgentType.Value,
            workdir,
            thread.Sandbox,
            sessionModel,
            cancellationToken).ConfigureAwait(false);

        if (started)
        {
            await _threadEvents.PublishAsync(
                threadId,
                new ServerSentEvent(
                    "ai_chat.session",
                    new AgentSessionStateInfo("ready", thread.AgentType.Value.ToString(), workdir)),
                cancellationToken).ConfigureAwait(false);

            // RF-002: queued prompts persist as events — a session respawn
            // rebuilds the in-memory queue from durable rows before dispatch.
            var queue = _promptQueues.GetOrAdd(threadId, _ => new PromptQueue());
            if (queue.Count == 0)
            {
                var eventRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatEvent>>();
                var persisted = await eventRepo.Query
                    .Where(e => e.ThreadId == thread.Id && e.Role == AiChatEventRole.Queued)
                    .OrderBy(e => e.CreatedAt)
                    .Select(e => e.Id.Value)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var eventId in persisted)
                {
                    queue.Enqueue(eventId);
                }
            }

            _ = TryDispatchNextAsync(threadId);
        }

        return started;
    }

    public async Task<bool> PromptAsync(
        string threadId,
        string text,
        string delivery = "queue",
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var threadRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatThread>>();
        var thread = await threadRepo.GetAsync(AiChatThreadId.From(threadId), cancellationToken).ConfigureAwait(false);

        if (thread is null || thread.Mode != "agent" || thread.AgentType is null)
        {
            return false;
        }

        var adapter = _adapters.FirstOrDefault(a => a.CanHandle(thread.AgentType.Value));
        if (adapter is not null && !adapter.SupportsInteractiveSession)
        {
            // Fallback one-shot (RF-008)
            return await ExecuteOneShotFallbackAsync(thread, text, cancellationToken).ConfigureAwait(false);
        }

        var ready = await EnsureSessionAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (!ready)
        {
            return false;
        }

        var sent = await _sessionClient.SendPromptAsync(threadId, text, delivery, cancellationToken).ConfigureAwait(false);
        if (sent)
        {
            // A session/prompt is in flight — queued items wait for its
            // stopReason event before dispatching.
            _promptQueues.GetOrAdd(threadId, _ => new PromptQueue()).MarkTurnActive();
        }

        return sent;
    }

    /// <summary>
    /// SPEC-20260921-ai-code-chat-ux RF-002: persists the prompt as a
    /// <c>queued</c> event and dispatches it when no turn is in flight. Returns
    /// the event dto so the composer can render the queued bubble immediately.
    /// </summary>
    public async Task<AiChatEventDto?> EnqueuePromptAsync(
        string threadId,
        string text,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var threadRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatThread>>();
        var thread = await threadRepo.GetAsync(AiChatThreadId.From(threadId), cancellationToken).ConfigureAwait(false);
        if (thread is null || thread.Mode != "agent" || thread.AgentType is null || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var eventRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatEvent>>();
        var queued = AiChatEvent.CreateTyped(
            AiChatEventId.NewGuid(),
            thread.Id,
            AiChatEventRole.Queued,
            text,
            AiChatEventKind.Message);
        await eventRepo.AddAsync(queued, cancellationToken).ConfigureAwait(false);
        await eventRepo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var dto = queued.ToDto();
        await _threadEvents.PublishAsync(
            threadId,
            new ServerSentEvent("ai_chat.event", dto),
            cancellationToken).ConfigureAwait(false);

        _promptQueues.GetOrAdd(threadId, _ => new PromptQueue()).Enqueue(queued.Id.Value);
        await TryDispatchNextAsync(threadId, cancellationToken).ConfigureAwait(false);
        return dto;
    }

    /// <summary>RF-002: cancels a queued prompt before dispatch.</summary>
    public async Task<bool> CancelQueuedPromptAsync(
        string threadId,
        string eventId,
        CancellationToken cancellationToken = default)
    {
        var removed = _promptQueues.TryGetValue(threadId, out var queue) && queue.Remove(eventId);

        using var scope = _scopeFactory.CreateScope();
        var eventRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatEvent>>();
        var evt = await eventRepo.GetAsync(AiChatEventId.From(eventId), cancellationToken).ConfigureAwait(false);
        if (evt is not null && evt.ThreadId.Value == threadId && evt.Role == AiChatEventRole.Queued)
        {
            await eventRepo.DeleteAsync(evt, cancellationToken).ConfigureAwait(false);
            await eventRepo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        return removed;
    }

    /// <summary>
    /// SPEC-20260921-ai-code-chat-ux RF-004: resends the most recent user
    /// prompt; an active turn is cancelled first.
    /// </summary>
    public async Task<bool> RetryLastPromptAsync(string threadId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var eventRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatEvent>>();
        var lastUserPrompt = await eventRepo.Query
            .Where(e => e.ThreadId == AiChatThreadId.From(threadId) && e.Role == AiChatEventRole.User)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.Content)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(lastUserPrompt))
        {
            return false;
        }

        if (_sessionClient.IsSessionActive(threadId))
        {
            await _sessionClient.CancelAsync(threadId, cancellationToken).ConfigureAwait(false);
            if (_promptQueues.TryGetValue(threadId, out var queue))
            {
                queue.Reset();
            }
        }

        return await PromptAsync(threadId, lastUserPrompt, "queue", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Dispatches the next queued prompt when the turn is idle. PromptQueue's
    /// atomic TryDispatch makes concurrent callers safe (NFR-002).
    /// </summary>
    private async Task TryDispatchNextAsync(string threadId, CancellationToken cancellationToken = default)
    {
        if (!_promptQueues.TryGetValue(threadId, out var queue)
            || !queue.TryDispatch(out var eventId)
            || eventId is null)
        {
            return;
        }

        try
        {
            var ready = await EnsureSessionAsync(threadId, cancellationToken).ConfigureAwait(false);
            string? text = null;
            AiChatEvent? evt = null;
            IRepository<AiChatEvent>? eventRepo = null;
            IServiceScope? scope = null;
            if (ready)
            {
                scope = _scopeFactory.CreateScope();
                eventRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatEvent>>();
                evt = await eventRepo.GetAsync(AiChatEventId.From(eventId), cancellationToken).ConfigureAwait(false);
                text = evt?.Content;
            }

            var sent = ready
                && text is not null
                && await _sessionClient.SendPromptAsync(threadId, text, "queue", cancellationToken).ConfigureAwait(false);

            if (!sent)
            {
                scope?.Dispose();
                queue.DispatchFailed(eventId);
                return;
            }

            if (evt is not null && eventRepo is not null)
            {
                evt.MarkDispatched();
                await eventRepo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await _threadEvents.PublishAsync(
                    threadId,
                    new ServerSentEvent("ai_chat.event", evt.ToDto()),
                    cancellationToken).ConfigureAwait(false);
            }

            scope?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch queued prompt '{EventId}' for thread '{ThreadId}'.", eventId, threadId);
            queue.DispatchFailed(eventId);
        }
    }

    private Task<bool> ExecuteOneShotFallbackAsync(AiChatThread thread, string text, CancellationToken cancellationToken)
    {
        var workdir = !string.IsNullOrWhiteSpace(thread.WorkspacePath)
            ? thread.WorkspacePath
            : (!string.IsNullOrWhiteSpace(thread.RepositoryFullName)
                ? _workspaceService.ResolveCardWorkdir(thread.RepositoryFullName, out _)
                : _workspaceService.EnsureRoot());

        var req = new Taskboard.Agents.AgentExecutionRequest(
            thread.Id.Value,
            0,
            thread.RepositoryFullName ?? "local",
            workdir,
            null,
            null,
            text,
            thread.AgentType!.Value);

        var progress = new Progress<Taskboard.Agents.AgentLogMessage>(async log =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var eventRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatEvent>>();
                var evt = AiChatEvent.CreateTyped(
                    AiChatEventId.NewGuid(),
                    thread.Id,
                    AiChatEventRole.Assistant,
                    log.Content,
                    log.Stream == Taskboard.Agents.AgentLogStream.StdErr ? AiChatEventKind.Error : AiChatEventKind.Message);

                await eventRepo.AddAsync(evt).ConfigureAwait(false);
                await eventRepo.SaveChangesAsync().ConfigureAwait(false);

                await _threadEvents.PublishAsync(thread.Id.Value, new ServerSentEvent("ai_chat.event", evt.ToDto())).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist fallback event for thread '{ThreadId}'.", thread.Id.Value);
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await _fallbackAcpClient.ExecuteAsync(req, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "One-shot fallback execution failed for thread '{ThreadId}'.", thread.Id.Value);
            }
        }, cancellationToken);

        return Task.FromResult(true);
    }

    public Task<bool> CancelAsync(string threadId, CancellationToken cancellationToken = default)
    {
        return _sessionClient.CancelAsync(threadId, cancellationToken);
    }

    /// <summary>RF-003: set a session config option (e.g. model) on the live session.</summary>
    public Task<bool> SetConfigOptionAsync(
        string threadId, string configId, string value, CancellationToken cancellationToken = default)
    {
        return _sessionClient.SetConfigOptionAsync(threadId, configId, value,
            cancellationToken: cancellationToken);
    }

    /// <summary>RF-003: switch session mode (plan/build/...) on the live session.</summary>
    public Task<bool> SetModeAsync(string threadId, string modeId, CancellationToken cancellationToken = default)
    {
        return _sessionClient.SetModeAsync(threadId, modeId, cancellationToken);
    }

    /// <summary>Negotiated peer capabilities/info for the thread's live session, if any.</summary>
    public AcpPeerInfo? GetPeerInfo(string threadId) => _sessionClient.GetPeerInfo(threadId);

    public Task StopSessionAsync(string threadId, CancellationToken cancellationToken = default)
    {
        _sessionClient.UnregisterEventListener(threadId);
        return _sessionClient.StopSessionAsync(threadId, cancellationToken);
    }

    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _reconnects = new();

    private void HandleSessionEvent(string threadId, AgentSessionEvent e)
    {
        // RF-002/011: a "dead" lifecycle event means the channel died while the
        // session was still wanted (deliberate stops unregister the listener
        // first) — respawn so session/resume can restore the context. Bounded
        // to 3 attempts per 5-minute window to avoid crash-loops.
        if (e.Kind == "lifecycle" && e.PayloadJson?.Contains("\"dead\"") == true)
        {
            _promptQueues.TryGetValue(threadId, out var deadQueue);
            deadQueue?.Reset();
            _ = Task.Run(() => TryReconnectAsync(threadId));
        }

        // RF-002: the turn boundary is the session/prompt response — free the
        // queue on stopReason or turn rejection, then dispatch the next item.
        var turnEnded =
            (e.Kind == "session" && e.PayloadJson?.Contains("\"stopReason\"") == true)
            || (e.Kind == "error" && e.Content?.StartsWith("Prompt turn rejected", StringComparison.Ordinal) == true)
            || (e.Kind == "session" && e.PayloadJson?.Contains("\"dead\"") == true);
        if (turnEnded)
        {
            var queue = _promptQueues.GetOrAdd(threadId, _ => new PromptQueue());
            if (e.Kind == "session" && e.PayloadJson?.Contains("\"dead\"") == true)
            {
                queue.Reset();
            }
            else
            {
                queue.TurnCompleted();
            }

            _ = Task.Run(() => TryDispatchNextAsync(threadId));
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // SPEC-20260921-agent-execution-event-pipeline RF-003: every
                // session event also enters the durable normalized stream,
                // keeping the ACP correlation fields (session/tool call).
                if (_eventSink is not null)
                {
                    await _eventSink.EmitAsync(new AgentExecutionEvent(
                        string.Empty, AgentEventScope.Thread, threadId, 0,
                        e.Timestamp,
                        string.IsNullOrWhiteSpace(e.Kind) ? AgentEventKinds.Message : e.Kind,
                        SessionId: e.SessionId,
                        ToolCallId: e.ToolCallId,
                        Title: e.Content,
                        PayloadJson: e.PayloadJson,
                        Stream: e.Kind == "error" ? "stderr" : "system",
                        MessageId: e.MessageId,
                        PlanId: e.PlanId,
                        PatchOp: e.PatchOp), CancellationToken.None).ConfigureAwait(false);
                }

                if (e.Kind == "permission" && !string.IsNullOrWhiteSpace(e.PayloadJson))
                {
                    var req = AcpSessionMessageParser.ParsePermissionRequest(e.PayloadJson);
                    if (req is not null)
                    {
                        var outcome = await _permissionGate.RequestPermissionAsync(
                            threadId,
                            req.Tool,
                            req.Detail,
                            req.Options).ConfigureAwait(false);

                        await _sessionClient.ReplyPermissionAsync(threadId, req.RequestId, outcome).ConfigureAwait(false);
                        return;
                    }
                }

                using var scope = _scopeFactory.CreateScope();
                var eventRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatEvent>>();

                var kind = AiChatEventKind.IsValid(e.Kind)
                    ? AiChatEventKind.From(e.Kind)
                    : AiChatEventKind.Message;

                var chatEvent = AiChatEvent.CreateTyped(
                    AiChatEventId.NewGuid(),
                    AiChatThreadId.From(threadId),
                    AiChatEventRole.Assistant,
                    string.IsNullOrWhiteSpace(e.Content) ? "(tool output)" : e.Content,
                    kind,
                    e.PayloadJson);

                await eventRepo.AddAsync(chatEvent).ConfigureAwait(false);
                await eventRepo.SaveChangesAsync().ConfigureAwait(false);

                await _threadEvents.PublishAsync(
                    threadId,
                    new ServerSentEvent("ai_chat.event", chatEvent.ToDto())).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist and publish agent session event for thread '{ThreadId}'.", threadId);
            }
        });
    }

    private async Task TryReconnectAsync(string threadId)
    {
        var now = DateTimeOffset.UtcNow;
        var state = _reconnects.AddOrUpdate(
            threadId,
            _ => (1, now),
            (_, prev) => now - prev.WindowStart > TimeSpan.FromMinutes(5)
                ? (1, now)
                : (prev.Count + 1, prev.WindowStart));

        if (state.Count > 3)
        {
            _logger.LogWarning(
                "Giving up reconnecting agent session for thread '{ThreadId}' after {Count} attempts.",
                threadId,
                state.Count);
            return;
        }

        _logger.LogInformation(
            "Respawning dead agent session for thread '{ThreadId}' (attempt {Count}).",
            threadId,
            state.Count);

        var ready = await EnsureSessionAsync(threadId).ConfigureAwait(false);
        if (ready)
        {
            // Only forgive the counter after the respawn proves stable — a
            // session that dies again inside the window keeps counting, so a
            // crash-looping agent is eventually left dead instead of
            // reconnecting forever.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                if (_sessionClient.IsSessionActive(threadId))
                {
                    _reconnects.TryRemove(threadId, out _);
                }
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reaperCts.Cancel();
        _reaperCts.Dispose();
        _sessionClient.Dispose();
        await Task.CompletedTask;
    }
}
