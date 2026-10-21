using Taskboard;
using Taskboard.Delegation;

namespace Taskboard.Domain.Entities.Delegation;

/// <summary>
/// SPEC-20261020-orchestration-contract-v2 RF-001: durable namespace of one
/// coordination — tasks, dispatches and deliveries hang off the run. Scope is
/// the owning conversation id (or "harness"); the coordinator key identifies
/// who drives it (assistant cli or "assistant").
/// </summary>
public sealed class DelegationRun : AggregateRoot<string>
{
    private DelegationRun(string id)
        : base(id)
    {
    }

    /// <summary>Owning scope — conversation id, or "harness".</summary>
    public string Scope { get; private set; } = string.Empty;
    public string Objective { get; private set; } = string.Empty;
    public string CoordinatorKey { get; private set; } = string.Empty;
    public DelegationRunStatus Status { get; private set; } = DelegationRunStatus.Open;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SettledAtUtc { get; private set; }

    public static DelegationRun Create(
        string scope, string objective, string coordinatorKey, DateTime? now = null) =>
        new($"run-{Guid.NewGuid():N}")
        {
            Scope = string.IsNullOrWhiteSpace(scope) ? "harness" : scope.Trim(),
            Objective = objective?.Trim() ?? string.Empty,
            CoordinatorKey = string.IsNullOrWhiteSpace(coordinatorKey) ? "assistant" : coordinatorKey.Trim(),
            CreatedAtUtc = now ?? DateTime.UtcNow,
        };

    public void Settle(DateTime? now = null)
    {
        if (Status != DelegationRunStatus.Open)
        {
            return;
        }

        Status = DelegationRunStatus.Settled;
        SettledAtUtc = now ?? DateTime.UtcNow;
    }

    public void Abort(DateTime? now = null)
    {
        if (Status != DelegationRunStatus.Open)
        {
            return;
        }

        Status = DelegationRunStatus.Aborted;
        SettledAtUtc = now ?? DateTime.UtcNow;
    }
}
