namespace Taskboard.Integrations.Execution;

/// <summary>Outcome of a single spawned process.</summary>
public sealed record ProcessRunResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut);

/// <summary>
/// Generic process spawner — <see cref="ProcessStartInfo.ArgumentList"/> only
/// (never shell-interpolated), environment scrubbed via
/// <see cref="WithoutTaskboardEnv"/>, timeout with process-tree kill.
/// Same mechanics as <c>GitCommandRunner</c> but binary-agnostic so the
/// verification engine can run <c>dotnet</c> and tests can fake outputs.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
