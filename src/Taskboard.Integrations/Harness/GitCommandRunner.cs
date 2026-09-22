using System.Diagnostics;
using Taskboard.Integrations.Execution;

namespace Taskboard.Integrations.Harness;

/// <inheritdoc cref="IGitCommandRunner"/>
public sealed class GitCommandRunner : IGitCommandRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly string _executable;

    public GitCommandRunner(string executable = "git")
    {
        _executable = executable;
    }

    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        WithoutHarnessEnv.RemoveFrom(startInfo.Environment);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {_executable}.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout ?? DefaultTimeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKill(process);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new GitCommandResult(
            process.HasExited ? process.ExitCode : -1,
            await stdout,
            await stderr,
            timedOut);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may have already exited.
        }
    }
}
