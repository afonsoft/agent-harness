using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Server.Hubs;

namespace Taskboard.Server.Services;

/// <summary>
/// <see cref="ICockpitEventStream"/> over <see cref="IHubContext{THub}"/> —
/// broadcasts to the run's SignalR group and keeps a bounded per-run buffer so
/// late joiners and reconnecting clients replay missed events via
/// `GET /api/harness/runs/{id}/events` (SPEC-20260919-ade-cockpit-hitl RF-001).
/// </summary>
public sealed class CockpitEventStream : ICockpitEventStream
{
    /// <summary>Max buffered events per run — oldest are dropped.</summary>
    internal const int BufferCap = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, ConcurrentQueue<CockpitEventDto>> _buffers = new();
    private readonly IHubContext<HarnessCockpitHub> _hub;
    private readonly ILogger<CockpitEventStream> _logger;

    public CockpitEventStream(IHubContext<HarnessCockpitHub> hub, ILogger<CockpitEventStream> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task PublishAsync(CockpitEventDto evt, CancellationToken cancellationToken = default)
    {
        var queue = _buffers.GetOrAdd(evt.RunId, _ => new ConcurrentQueue<CockpitEventDto>());
        queue.Enqueue(evt);
        while (queue.Count > BufferCap && queue.TryDequeue(out _))
        {
        }

        try
        {
            await _hub.Clients.Group(evt.RunId)
                .SendAsync("ReceiveCockpitEvent", evt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A dead group must never break the producing pipeline.
            _logger.LogDebug(ex, "Cockpit event {Kind} for run {RunId} could not be broadcast.", evt.Kind, evt.RunId);
        }
    }

    public async Task PublishApprovalAsync(ApprovalRequestDto request, CancellationToken cancellationToken = default)
    {
        await PublishAsync(
            new CockpitEventDto(
                request.RunId,
                DateTimeOffset.UtcNow,
                "approval",
                request.Title,
                JsonSerializer.Serialize(request, JsonOptions)),
            cancellationToken).ConfigureAwait(false);

        try
        {
            await _hub.Clients.Group(request.RunId)
                .SendAsync("RequireApproval", request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Approval request {RequestId} for run {RunId} could not be broadcast.", request.RequestId, request.RunId);
        }
    }

    public IReadOnlyList<CockpitEventDto> GetBuffered(string runId) =>
        _buffers.TryGetValue(runId, out var queue) ? queue.ToArray() : [];
}
