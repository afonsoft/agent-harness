using Taskboard;
using Taskboard.Delegation;

namespace Taskboard.Domain.Entities.Delegation;

/// <summary>
/// SPEC-20261020-orchestration-contract-v2 RF-002: one attempt of a
/// <see cref="DelegationTask"/> — the completion authority for
/// <c>worker_done</c>/heartbeat signals. <see cref="PreambleJson"/> carries the
/// task spec, the paired ids, the mailbox address and the contract text given
/// to the worker.
/// </summary>
public sealed class Dispatch : AggregateRoot<string>
{
    private Dispatch(string id)
        : base(id)
    {
    }

    public string TaskId { get; private set; } = string.Empty;
    public string RunId { get; private set; } = string.Empty;
    public string WorkerKey { get; private set; } = string.Empty;
    public int Attempt { get; private set; }
    public DispatchState State { get; private set; } = DispatchState.Pending;
    public string PreambleJson { get; private set; } = "{}";

    /// <summary>Terminal reuse handle (P3 — <c>delegate_task --terminal</c>).</summary>
    public string? TerminalHandle { get; private set; }

    /// <summary>Archived worker output — survives release (RF-006 read).</summary>
    public string? ArchivedOutput { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? DispatchedAtUtc { get; private set; }
    public DateTime? SettledAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }

    public static Dispatch Create(
        string taskId, string runId, string workerKey, int attempt,
        string preambleJson, DateTime? now = null, string? id = null)
    {
        if (attempt < 1)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "attempt must be >= 1");
        }

        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "taskId is required");
        }

        return new Dispatch(id ?? $"disp-{Guid.NewGuid():N}")
        {
            TaskId = taskId.Trim(),
            RunId = runId?.Trim() ?? string.Empty,
            WorkerKey = workerKey?.Trim() ?? string.Empty,
            Attempt = attempt,
            PreambleJson = string.IsNullOrWhiteSpace(preambleJson) ? "{}" : preambleJson,
            CreatedAtUtc = now ?? DateTime.UtcNow,
        };
    }

    /// <summary>pending → dispatched — the worker has been handed the preamble.</summary>
    public void MarkDispatched(DateTime? now = null)
    {
        if (State == DispatchState.Pending)
        {
            State = DispatchState.Dispatched;
            DispatchedAtUtc = now ?? DateTime.UtcNow;
        }
    }

    /// <summary>pending|dispatched → settled — worker_done accepted (or reconciled).</summary>
    public void Settle(DateTime? now = null)
    {
        if (State is not (DispatchState.Pending or DispatchState.Dispatched))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"cannot settle a {State.ToString().ToLowerInvariant()} dispatch");
        }

        State = DispatchState.Settled;
        SettledAtUtc ??= now ?? DateTime.UtcNow;
    }

    /// <summary>settled → retained — worker kept alive for follow-up.</summary>
    public void Retain()
    {
        if (State != DispatchState.Settled)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"cannot retain a {State.ToString().ToLowerInvariant()} dispatch");
        }

        State = DispatchState.Retained;
    }

    /// <summary>settled|retained → released — output archived, worker freed.</summary>
    public void Release(string? archivedOutput = null, DateTime? now = null)
    {
        if (State is not (DispatchState.Settled or DispatchState.Retained))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"cannot release a {State.ToString().ToLowerInvariant()} dispatch");
        }

        if (archivedOutput is not null)
        {
            ArchivedOutput = archivedOutput;
        }

        State = DispatchState.Released;
        ReleasedAtUtc = now ?? DateTime.UtcNow;
    }

    /// <summary>any non-released state → abandoned.</summary>
    public void Abandon(DateTime? now = null)
    {
        if (State == DispatchState.Released)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "cannot abandon a released dispatch");
        }

        State = DispatchState.Abandoned;
        SettledAtUtc ??= now ?? DateTime.UtcNow;
    }

    public void AttachTerminal(string handle) =>
        TerminalHandle = string.IsNullOrWhiteSpace(handle) ? TerminalHandle : handle.Trim();
}
