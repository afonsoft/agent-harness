using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Server-side cockpit event stream (SPEC-20260919-ade-cockpit-hitl RF-001):
/// producers (<see cref="ICockpitEventSink"/>) publish structured events that
/// are broadcast over the run's SignalR group AND kept in a bounded buffer so
/// late-joining/reconnecting clients can replay what they missed.
/// </summary>
public interface ICockpitEventStream
{
    /// <summary>Publishes an event to the run's cockpit group and buffers it for replay.</summary>
    Task PublishAsync(CockpitEventDto evt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a `RequireApproval` client call (RF-004) and also buffers it
    /// as a `approval` cockpit event for replay.
    /// </summary>
    Task PublishApprovalAsync(ApprovalRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Buffered events of a run in chronological order (bounded — newest kept).</summary>
    IReadOnlyList<CockpitEventDto> GetBuffered(string runId);
}
