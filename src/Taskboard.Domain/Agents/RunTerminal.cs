using Taskboard;
using Taskboard.Agents;

namespace Taskboard.Domain.Agents;

/// <summary>
/// SPEC-20261104-run-terminal-runtime RF-001: durable record of a delegated
/// run's persistent PTY — the handle that makes a run watchable, steerable
/// and available for another turn. One active row per dispatch; the row is
/// retained after finish for reads. Live process facts (scrollback, viewers)
/// stay in the terminal session manager; this row carries the control state
/// a restart must not lose (ownership, first-message delivery, exit).
/// </summary>
public sealed class RunTerminal : AggregateRoot<string>
{
    private RunTerminal(string id)
        : base(id)
    {
    }

    public string TaskId { get; private set; } = string.Empty;
    public string? DispatchId { get; private set; }
    public string PtySessionId { get; private set; } = string.Empty;
    public string CliName { get; private set; } = string.Empty;
    public RunTerminalState State { get; private set; } = RunTerminalState.Live;
    public RunTerminalControlOwner ControlOwner { get; private set; } = RunTerminalControlOwner.Operator;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public int? ExitCode { get; private set; }

    /// <summary>Scrollback tail captured at finish — read-after-exit source.</summary>
    public string? ArchivedOutput { get; private set; }

    public static RunTerminal Create(
        string taskId, string? dispatchId, string ptySessionId, string cliName,
        DateTime? now = null, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "taskId is required");
        }

        if (string.IsNullOrWhiteSpace(ptySessionId))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "ptySessionId is required");
        }

        return new RunTerminal(id ?? $"rt-{Guid.NewGuid():N}")
        {
            TaskId = taskId.Trim(),
            DispatchId = string.IsNullOrWhiteSpace(dispatchId) ? null : dispatchId.Trim(),
            PtySessionId = ptySessionId.Trim(),
            CliName = cliName?.Trim() ?? string.Empty,
            CreatedAtUtc = now ?? DateTime.UtcNow,
        };
    }

    /// <summary>First human keystroke on an attached viewer flips control (RF-007).</summary>
    public void TakeHumanControl()
    {
        if (State == RunTerminalState.Live)
        {
            ControlOwner = RunTerminalControlOwner.Human;
        }
    }

    /// <summary>All viewers detached — the orchestrator drives again (RF-007).</summary>
    public void ReleaseHumanControl()
    {
        if (ControlOwner == RunTerminalControlOwner.Human)
        {
            ControlOwner = RunTerminalControlOwner.Operator;
        }
    }

    /// <summary>The PTY exited or was killed — the row stays for archived reads.</summary>
    public void Finish(int? exitCode, string? archivedOutput, DateTime? now = null)
    {
        if (State == RunTerminalState.Finished)
        {
            return;
        }

        State = RunTerminalState.Finished;
        ExitCode = exitCode;
        ArchivedOutput = archivedOutput ?? ArchivedOutput;
        FinishedAtUtc = now ?? DateTime.UtcNow;
        ControlOwner = RunTerminalControlOwner.Operator;
    }
}
