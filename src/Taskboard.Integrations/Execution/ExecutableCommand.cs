using System.Diagnostics;

namespace Taskboard.Integrations.Execution;

public sealed class ExecutableCommand
{
    private readonly string _executable;
    private readonly string _arguments;

    public ExecutableCommand(string executable, string arguments)
    {
        _executable = executable;
        _arguments = arguments;
    }

    public async Task<int> RunAsync(Action<string>? onOutput = null, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(_executable, _arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        WithoutHarnessEnv.RemoveFrom(startInfo.Environment);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Failed to start {_executable}.");
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onOutput?.Invoke(e.Data);
            }
        };

        process.BeginOutputReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return process.ExitCode;
    }
}
