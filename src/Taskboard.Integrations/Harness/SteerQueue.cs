using System.Collections.Concurrent;
using Taskboard.Application.Contracts.Harness;

namespace Taskboard.Integrations.Harness;

/// <summary>
/// In-memory <see cref="ISteerQueue"/> — pending instructions are folded into
/// the next dispatched stage prompt (SPEC-20260919-ade-cockpit-hitl RF-003).
/// </summary>
public sealed class SteerQueue : ISteerQueue
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _queues = new();

    public void Enqueue(string runId, string instruction)
    {
        if (!string.IsNullOrWhiteSpace(instruction))
        {
            _queues.GetOrAdd(runId, _ => new ConcurrentQueue<string>()).Enqueue(instruction);
        }
    }

    public IReadOnlyList<string> Drain(string runId)
    {
        if (!_queues.TryRemove(runId, out var queue))
        {
            return [];
        }

        var instructions = new List<string>();
        while (queue.TryDequeue(out var instruction))
        {
            instructions.Add(instruction);
        }

        return instructions;
    }
}
