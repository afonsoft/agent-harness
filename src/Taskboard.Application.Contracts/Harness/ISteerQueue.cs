namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Pending human-steer instructions per run (SPEC-20260919-ade-cockpit-hitl
/// RF-003): when the running agent has no live injection channel the
/// instruction is queued and folded into the next dispatched stage prompt.
/// In-memory — instructions enqueued while no stage is running are still
/// consumed by the next eligible stage of the same process lifetime.
/// </summary>
public interface ISteerQueue
{
    /// <summary>Queues an instruction for the run's next stage prompt.</summary>
    void Enqueue(string runId, string instruction);

    /// <summary>Returns and removes every queued instruction for the run.</summary>
    IReadOnlyList<string> Drain(string runId);
}
