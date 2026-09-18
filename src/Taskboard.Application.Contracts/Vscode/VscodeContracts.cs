using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Application.Contracts.Vscode;

/// <summary>Snapshot of the code-server install/runtime state (SPEC-20260917-vscode-web-workspace).</summary>
public sealed record VscodeStatus(
    bool Installed,
    string? BinaryPath,
    string? Version,
    bool Running,
    int Port,
    string HomeDirectory,
    string WorkspaceRoot);

/// <summary>Snapshot of the most recent code-server install run.</summary>
public sealed record VscodeInstallStatus(
    AgentCliInstallState State,
    DateTimeOffset? StartedAtUtc,
    int? ExitCode,
    IReadOnlyList<AgentCliInstallLine> Lines);

/// <summary>Resolved workdir of a repository card under the workspace root.</summary>
public sealed record VscodeWorkdir(string Path, bool Exists);

/// <summary>
/// Runs the fixed, allowlisted code-server install command in the background —
/// same lifecycle as <see cref="IAgentCliInstallService"/> but for the editor.
/// </summary>
public interface IVscodeInstallService
{
    /// <summary>Starts the install when none is running; returns the live snapshot otherwise.</summary>
    Task<VscodeInstallStatus> StartInstallAsync(CancellationToken cancellationToken = default);

    /// <summary>Current snapshot (<see cref="AgentCliInstallState.Idle"/> before any run).</summary>
    VscodeInstallStatus GetStatus();
}

/// <summary>
/// Manages the code-server child process: binary probe, lazy spawn on loopback
/// (<c>--auth none</c> — the Taskboard auth in front of the proxy is the gate).
/// </summary>
public interface ICodeServerManager
{
    /// <summary>Current status — binary probe + process state.</summary>
    Task<VscodeStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Spawns code-server when installed and not already running.
    /// Returns the post-attempt status (<c>Running=false</c> when not installed or failed).
    /// </summary>
    Task<VscodeStatus> EnsureStartedAsync(CancellationToken cancellationToken = default);
}
