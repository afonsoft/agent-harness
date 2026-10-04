using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Delegation;

/// <summary>
/// SPEC-20261006 RF-001: agent dashboard — four attention columns built from
/// delegation tasks, agent runs and the mailbox.
/// </summary>
public sealed record AgentDashboardDto(
    IReadOnlyList<DashboardItemDto> NeedsYou,
    IReadOnlyList<DashboardItemDto> Working,
    IReadOnlyList<DashboardItemDto> Done,
    IReadOnlyList<DashboardItemDto> Idle);

/// <summary>One dashboard card: a delegation task, an agent run, a mailbox message or an idle CLI.</summary>
public sealed record DashboardItemDto(
    /// <summary>task | run | mailbox | cli</summary>
    string Kind,
    /// <summary>Task id, run id, message id or cli kind.</summary>
    string Id,
    string Title,
    string Detail,
    string CliName,
    DateTime? Timestamp);

/// <summary>Aggregates the dashboard columns for one scope.</summary>
public interface IDelegationDashboardService
{
    /// <summary>Builds the four attention columns for <paramref name="scope"/>.</summary>
    Task<AgentDashboardDto> GetAsync(string scope, int take = 20, CancellationToken ct = default);
}
