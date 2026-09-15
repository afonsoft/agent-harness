using System.Diagnostics;
using System.Text;
using Taskboard.Integrations.Agents;

namespace Taskboard.Integrations.Skills;

internal sealed record GitResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Thin wrapper over the <c>git</c> CLI. Authentication for private GitHub
/// repositories is injected via <c>http.extraheader</c> so the token never
/// lands in the remote URL, and stderr is scrubbed before it reaches logs.
/// </summary>
internal static class GitRunner
{
    public static string? FindGit() => PathSearch.FindExecutable("git");

    public static async Task<GitResult> RunAsync(
        string workingDirectory,
        string? authToken,
        CancellationToken cancellationToken,
        params string[] args)
    {
        var git = FindGit()
            ?? throw new InvalidOperationException("git executable was not found on PATH.");

        var startInfo = new ProcessStartInfo(git)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(authToken))
        {
            // GitHub's git-over-HTTP endpoint rejects the Bearer scheme; it
            // expects Basic credentials — same shape actions/checkout uses.
            var basic = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"x-access-token:{authToken}"));
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(
                $"http.https://github.com/.extraheader=AUTHORIZATION: basic {basic}");
        }

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the git process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stderr = await stderrTask.ConfigureAwait(false);
        if (!string.IsNullOrEmpty(authToken))
        {
            stderr = stderr.Replace(authToken, "***", StringComparison.Ordinal);
        }

        return new GitResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), stderr);
    }
}
