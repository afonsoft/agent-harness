using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-002: hosted executor for provider
/// chat runs. The enqueue endpoint only persists the trigger message and the
/// queued <see cref="ChatRun"/> — this service executes it detached from any
/// HTTP request, so closing the browser never cancels a turn.
/// Ordering: a per-conversation task chain guarantees FIFO and one live run
/// per conversation; a global semaphore caps concurrent runs across
/// conversations (<c>Taskboard:Chat:Runs:MaxConcurrent</c>).
/// </summary>
public sealed class ChatRunDispatcherService : BackgroundService
{
    internal const string ConfigSection = "Taskboard:Chat:Runs";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChatRunQueue _queue;
    private readonly ChatRunBroadcaster _broadcaster;
    private readonly ChatRunCoordinator _coordinator;
    private readonly ILogger<ChatRunDispatcherService> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _slots;
    private readonly int _checkpointMs;
    private readonly ConcurrentDictionary<string, Task> _conversationTails = new(StringComparer.Ordinal);

    public ChatRunDispatcherService(
        IServiceScopeFactory scopeFactory,
        ChatRunQueue queue,
        ChatRunBroadcaster broadcaster,
        ChatRunCoordinator coordinator,
        IConfiguration configuration,
        ILogger<ChatRunDispatcherService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _broadcaster = broadcaster;
        _coordinator = coordinator;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _slots = new SemaphoreSlim(
            Math.Max(1, configuration.GetValue($"{ConfigSection}:MaxConcurrent", 4)));
        _checkpointMs = Math.Max(100,
            configuration.GetValue($"{ConfigSection}:CheckpointMs", 750));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Boot sweep: runs left Queued/Running by a dead process are orphans.
        await InterruptOrphansAsync(stoppingToken).ConfigureAwait(false);

        try
        {
            await foreach (var item in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                Schedule(item, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown — live runs see the same token and land as Interrupted.
        }
    }

    /// <summary>Marks leftover Queued/Running rows Interrupted — internal for tests.</summary>
    internal async Task InterruptOrphansAsync(CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<ChatRun>>();
        var orphans = await repo.Query
            .Where(r => r.Status == ChatRunStatus.Queued || r.Status == ChatRunStatus.Running)
            .ToListAsync(ct).ConfigureAwait(false);
        if (orphans.Count == 0)
        {
            return;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var run in orphans)
        {
            run.Interrupt(now);
        }

        await repo.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        _logger.LogInformation("chat runs: marked {Count} orphaned run(s) interrupted", orphans.Count);
    }

    /// <summary>
    /// Chains the work item after the conversation's previous run — FIFO with
    /// exactly one live run per conversation, regardless of task scheduling.
    /// </summary>
    private void Schedule(ChatRunWorkItem item, CancellationToken stoppingToken)
    {
        _conversationTails.AddOrUpdate(
            item.ConversationId,
            _ => ExecuteItemAsync(item, stoppingToken),
            (_, tail) => tail
                .ContinueWith(
                    _ => ExecuteItemAsync(item, stoppingToken),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default)
                .Unwrap());
    }

    private async Task ExecuteItemAsync(ChatRunWorkItem item, CancellationToken stoppingToken)
    {
        await _slots.WaitAsync(stoppingToken).ConfigureAwait(false);
        try
        {
            await RunItemAsync(item, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown mid-acquire — the row stays Queued for the next boot sweep.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "chat run {RunId} crashed outside the executor", item.RunId);
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task RunItemAsync(ChatRunWorkItem item, CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<ChatRun>>();
        var executor = scope.ServiceProvider.GetRequiredService<IChatRunExecutor>();
        var notifiers = scope.ServiceProvider
            .GetServices<IChatRunNotifier>()
            .ToList();

        var run = await repo.GetAsync(ChatRunId.From(item.RunId), stoppingToken).ConfigureAwait(false);
        if (run is null || run.Status != ChatRunStatus.Queued)
        {
            return; // vanished or stopped while queued — nothing to do.
        }

        var cts = await _coordinator.BeginAsync(item.ConversationId).ConfigureAwait(false);
        run.Start(_time.GetUtcNow().UtcDateTime);
        await repo.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

        ChatDoneEvent? done = null;
        Exception? failure = null;
        var lastCheckpoint = _time.GetUtcNow();
        try
        {
            await foreach (var chatEvent in executor.ExecuteAsync(run, cts, stoppingToken).ConfigureAwait(false))
            {
                _broadcaster.Publish(run.Id.Value, chatEvent);
                if (chatEvent is ChatDoneEvent doneEvent)
                {
                    done = doneEvent;
                }

                // Throttled checkpoint — always on tool boundaries and done,
                // so the attach replay never loses more than CheckpointMs of text.
                var elapsedMs = (_time.GetUtcNow() - lastCheckpoint).TotalMilliseconds;
                if (elapsedMs >= _checkpointMs
                    || chatEvent is ChatToolCallEvent or ChatToolResultEvent or ChatDoneEvent)
                {
                    var live = _broadcaster.GetLive(run.Id.Value);
                    run.Checkpoint(live?.Partial, live?.PartialReasoning);
                    await repo.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                    lastCheckpoint = _time.GetUtcNow();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            if (run.Status.IsActive)
            {
                run.Interrupt(_time.GetUtcNow().UtcDateTime);
                await repo.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "chat run {RunId} failed inside the executor", item.RunId);
            failure = ex;
        }
        finally
        {
            var now = _time.GetUtcNow().UtcDateTime;
            if (run.Status.IsActive)
            {
                if (failure is not null)
                {
                    run.Fail(failure.Message, now);
                }
                else if (done?.Error == "stopped by user")
                {
                    run.Stop(now);
                }
                else if (done?.Error is not null)
                {
                    run.Fail(done.Error, now);
                }
                else if (done is not null)
                {
                    run.Complete(done.TokensIn, done.TokensOut, now);
                }
                else
                {
                    run.Fail("run ended without a terminal event", now);
                }

                await repo.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }

            _broadcaster.Complete(run.Id.Value);
            _coordinator.End(item.ConversationId, cts);
            cts.Dispose();
        }

        if (run.Status.IsTerminal)
        {
            foreach (var notifier in notifiers)
            {
                try
                {
                    await notifier.RunCompletedAsync(run, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "chat run {RunId} completion notify failed", item.RunId);
                }
            }
        }
    }
}
