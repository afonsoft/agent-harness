using System.Diagnostics;

namespace Taskboard.Integrations.Skills;

/// <summary>Result of a spawned command (stdout/stderr captured, never shell-expanded).</summary>
public sealed record CommandResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Abstraction over process spawning so the installer flow is unit-testable
/// without launching real processes (SPEC-20260917-skills-installer).
/// </summary>
public interface ISkillsInstallRunner
{
    Task<CommandResult> RunAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

/// <summary>Default runner: spawns the process with argument arrays (no shell).</summary>
public sealed class ProcessSkillsInstallRunner : ISkillsInstallRunner
{
    public static readonly ProcessSkillsInstallRunner Instance = new();

    public async Task<CommandResult> RunAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{executable}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new CommandResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }
}
