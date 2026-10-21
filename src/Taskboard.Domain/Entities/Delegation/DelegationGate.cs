using Taskboard.Delegation;

namespace Taskboard.Domain.Entities.Delegation;

/// <summary>
/// SPEC-20261020-v2 RF-005: a task-bound gate — while Open the task stays
/// Blocked; Resolve releases the task back to the DAG verdict.
/// </summary>
public sealed class DelegationGate
{
    private DelegationGate() { }

    public string Id { get; private set; } = default!;
    public string TaskId { get; private set; } = default!;

    /// <summary>Mailbox/DAG scope inherited from the task — scope-wide listing.</summary>
    public string Scope { get; private set; } = default!;
    public string Question { get; private set; } = default!;

    /// <summary>Serialized options array; null = free-form resolution.</summary>
    public string? OptionsJson { get; private set; }
    public DelegationGateStatus Status { get; private set; }
    public string? Resolution { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public long Version { get; private set; }

    public static DelegationGate Create(
        string taskId, string scope, string question, string? optionsJson,
        DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "taskId is required");
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "question is required");
        }

        return new DelegationGate
        {
            Id = $"gate-{Guid.NewGuid():N}",
            TaskId = taskId.Trim(),
            Scope = string.IsNullOrWhiteSpace(scope) ? "harness" : scope.Trim(),
            Question = question.Trim(),
            OptionsJson = optionsJson,
            Status = DelegationGateStatus.Open,
            CreatedAtUtc = now ?? DateTime.UtcNow,
        };
    }

    /// <summary>Resolve with a chosen option/free-form text — terminal.</summary>
    public DelegationGate Resolve(string resolution, DateTime? now = null)
    {
        if (Status is DelegationGateStatus.Resolved)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"gate {Id} is already resolved");
        }

        if (string.IsNullOrWhiteSpace(resolution))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "resolution is required");
        }

        Status = DelegationGateStatus.Resolved;
        Resolution = resolution.Trim();
        ResolvedAtUtc = now ?? DateTime.UtcNow;
        Version++;
        return this;
    }
}
