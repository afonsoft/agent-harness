using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>Lifecycle of an agent-CLI install run (SPEC-20260918-cli-agents-expansion RF-005).</summary>
public enum AgentCliInstallState
{
    Idle,
    Running,
    Succeeded,
    Failed
}

/// <summary>One captured stdout/stderr line from an install run.</summary>
public sealed record AgentCliInstallLine(DateTimeOffset AtUtc, string Stream, string Content);

/// <summary>Snapshot of the most recent install run for a CLI kind.</summary>
public sealed record AgentCliInstallStatus(
    AgentCliKind Kind,
    AgentCliInstallState State,
    DateTimeOffset? StartedAtUtc,
    int? ExitCode,
    IReadOnlyList<AgentCliInstallLine> Lines);

/// <summary>
/// Runs the fixed, server-side allowlisted install command for a CLI kind
/// (SPEC-20260918-cli-agents-expansion RF-004/RF-006/RF-012). At most one run
/// per kind executes at a time.
/// </summary>
public interface IAgentCliInstallService
{
    /// <summary>
    /// Starts the install for <paramref name="kind"/> when none is running.
    /// Returns the current run status (idempotent — a running install returns
    /// its live snapshot instead of starting a second one).
    /// </summary>
    Task<AgentCliInstallStatus> StartInstallAsync(AgentCliKind kind, CancellationToken cancellationToken = default);

    /// <summary>Current snapshot for <paramref name="kind"/> (<see cref="AgentCliInstallState.Idle"/> before any run).</summary>
    AgentCliInstallStatus GetStatus(AgentCliKind kind);
}
