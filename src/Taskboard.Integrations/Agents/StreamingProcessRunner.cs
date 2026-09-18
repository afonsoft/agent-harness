using System.Diagnostics;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Process runner that forwards each stdout/stderr line to a callback as it
/// arrives — used by the CLI install flow so the popup shows live output
/// (SPEC-20260918-cli-agents-expansion RF-006).
/// </summary>
public interface IStreamingProcessRunner
{
    Task<int> RunAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        Action<string, string>? onLine,
        CancellationToken cancellationToken);
}

/// <summary>Default runner: spawns the process with argument arrays (no shell expansion).</summary>
public sealed class StreamingProcessRunner : IStreamingProcessRunner
{
    public static readonly StreamingProcessRunner Instance = new();

    public async Task<int> RunAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        Action<string, string>? onLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onLine?.Invoke("stdout", e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onLine?.Invoke("stderr", e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        process.WaitForExit(); // flush async output handlers
        return process.ExitCode;
    }
}
