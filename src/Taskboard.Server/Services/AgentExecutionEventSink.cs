using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Taskboard.Agents;
using Taskboard.Server.Hubs;

namespace Taskboard.Server.Services;

/// <summary>
/// <see cref="IAgentExecutionEventSink"/> singleton: assigns per-scope Sequence
/// (seeded from the persisted max — survives restart), redacts and truncates
/// payloads, persists via <see cref="IAgentRunEventRepository"/> and broadcasts
/// <c>ReceiveAgentEvent</c> to the SignalR group <c>agent:{scopeKind}:{scopeId}</c>.
/// Persist/broadcast failures are logged, never propagated to the producer
/// (SPEC-20260921-agent-execution-event-pipeline RF-003/RF-005).
/// </summary>
public sealed class AgentExecutionEventSink : IAgentExecutionEventSink
{
    public const int MaxPayloadChars = 32 * 1024;
    public const int MaxRawChars = 8 * 1024;
    public const string TruncatedMarker = "…[truncated]";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<AgentLogHub> _hub;
    private readonly ISecretRedactor _redactor;
    private readonly ILogger<AgentExecutionEventSink> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<string, long> _sequences = new();

    public AgentExecutionEventSink(
        IServiceScopeFactory scopeFactory,
        IHubContext<AgentLogHub> hub,
        ISecretRedactor redactor,
        ILogger<AgentExecutionEventSink> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _redactor = redactor;
        _logger = logger;
    }

    public static string GroupName(string scopeKind, string scopeId) => $"agent:{scopeKind}:{scopeId}";

    public async Task<AgentExecutionEvent> EmitAsync(AgentExecutionEvent evt, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(ScopeKey(evt), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sequence = await NextSequenceAsync(evt, cancellationToken).ConfigureAwait(false);
            var persisted = evt with
            {
                EventId = Guid.NewGuid().ToString("N"),
                Sequence = sequence,
                PayloadJson = Truncate(_redactor.Redact(evt.PayloadJson), MaxPayloadChars),
                RawJson = Truncate(_redactor.Redact(evt.RawJson), MaxRawChars),
                Title = Truncate(_redactor.Redact(evt.Title), 512)
            };

            if (!await PersistAsync(persisted).ConfigureAwait(false))
            {
                // Roll the counter back so the next event reuses this sequence —
                // replay must never have permanent gaps. The event is also not
                // broadcast: live subscribers see the same stream replay does.
                _sequences[ScopeKey(evt)] = sequence - 1;
                return persisted;
            }

            await BroadcastAsync(persisted, cancellationToken).ConfigureAwait(false);
            return persisted;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentExecutionEvent>> GetEventsAsync(
        string scopeKind, string scopeId, long after = 0, int take = 500, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentRunEventRepository>();
        return await repository.GetPageAsync(scopeKind, scopeId, after, Math.Clamp(take, 1, 1000), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<long> NextSequenceAsync(AgentExecutionEvent evt, CancellationToken cancellationToken)
    {
        var key = ScopeKey(evt);
        if (_sequences.TryGetValue(key, out var current))
        {
            var next = current + 1;
            _sequences[key] = next;
            return next;
        }

        long seeded = 0;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAgentRunEventRepository>();
            seeded = await repository.GetMaxSequenceAsync(evt.ScopeKind, evt.ScopeId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed event sequence for {Scope}; starting at 1.", SanitizeForLog(key));
        }

        var seededNext = seeded + 1;
        _sequences[key] = seededNext;
        return seededNext;
    }

    private async Task<bool> PersistAsync(AgentExecutionEvent evt)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAgentRunEventRepository>();
            await repository.AppendAsync(evt, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent event {Kind} seq {Seq} for {Scope} could not be persisted.",
                evt.Kind, evt.Sequence, SanitizeForLog(ScopeKey(evt)));
            return false;
        }
    }

    private async Task BroadcastAsync(AgentExecutionEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients.Group(GroupName(evt.ScopeKind, evt.ScopeId))
                .SendAsync("ReceiveAgentEvent", evt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Agent event {Kind} for {Scope} could not be broadcast.",
                evt.Kind, SanitizeForLog(ScopeKey(evt)));
        }
    }

    private static string ScopeKey(AgentExecutionEvent evt) => $"{evt.ScopeKind}:{evt.ScopeId}";

    // Scope ids are user-controlled — strip CR/LF before they reach log sinks.
    private static string SanitizeForLog(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');

    private static string? Truncate(string? value, int max)
    {
        if (value is null || value.Length <= max)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, max), TruncatedMarker);
    }
}
