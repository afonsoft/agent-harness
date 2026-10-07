using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Taskboard.Agents;
using Taskboard.Integrations.Execution;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Harness;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Runs a shell command in the workspace — auto-confined (RF-006): the
/// command is classified by the security gateway first (Dangerous → refused,
/// WorkspaceWrite → allowed only inside the workspace), executed with a
/// timeout and a killed process tree, output truncated and secret-scrubbed.
/// </summary>
public sealed class ShellExecTool(
    ICommandRiskClassifier classifier,
    ISecretRedactor redactor,
    IChatJobService? jobs = null,
    IConfiguration? configuration = null) : IChatTool
{
    internal const int MaxOutputChars = 16_000;

    public string Name => "shell_exec";
    public string Description =>
        "Execute a shell command now inside the workspace directory and get its real "
        + "stdout/stderr back. Use this to actually run commands the task needs (builds, "
        + "tests, git, ls, cat, installs) — never answer with the command for the user "
        + "to run; call this tool and report the output. Dangerous commands (recursive "
        + "deletes, sudo, network egress) are refused by the security gateway. "
        + "run_in_background detaches the command into a durable ChatJob — poll it with "
        + "job_list/job_output, stop it with job_kill.";
    public string ParametersJson => """
        {"type":"object","properties":{"command":{"type":"string","description":"The shell command line to run"},"timeout_seconds":{"type":"integer","description":"Execution timeout in seconds (default 60, max 300)"},"run_in_background":{"type":"boolean","description":"Detach into a durable background job that outlives this run"}},"required":["command"]}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var command = arguments.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ChatToolResult(ErrorJson("command is required"), Refused: true, "empty command");
        }

        var assessment = classifier.Classify(command, context.WorkspacePath);
        if (assessment.RiskLevel == SecurityRiskLevel.Dangerous || assessment.EscapesSandbox)
        {
            return new ChatToolResult(
                ErrorJson($"refused by security gateway: {assessment.Reason}"),
                Refused: true,
                assessment.Reason);
        }

        // SPEC-20261005-chat-jobs-schedule-search RF-001: detach into a
        // durable ChatJob — same gateway above, no timeout cap, output to a
        // per-job file, completion note on the transcript.
        var runInBackground = arguments.TryGetProperty("run_in_background", out var bg)
            && bg.ValueKind is JsonValueKind.True;
        if (runInBackground)
        {
            if (configuration is not null
                && !ChatFeatureFlags.IsEnabled(configuration, ChatFeatureFlags.JobsEnabledKey))
            {
                return new ChatToolResult(
                    ErrorJson("chat jobs are disabled"), Refused: true, "jobs disabled");
            }

            if (jobs is null || context.ConversationId is null)
            {
                return new ChatToolResult(
                    ErrorJson("background jobs unavailable in this context"),
                    Refused: true, "no job service");
            }

            var job = await jobs.StartAsync(
                context.ConversationId, context.RunId, command, context.WorkspacePath,
                cancellationToken).ConfigureAwait(false);
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                jobId = job.Id,
                status = "running",
            }));
        }

        var timeoutSeconds = arguments.TryGetProperty("timeout_seconds", out var t) && t.TryGetInt32(out var tv)
            ? Math.Clamp(tv, 5, 300)
            : 60;
        var result = await ChatProcessRunner.RunAsync("/bin/sh", ["-c", command], context.WorkspacePath,
            TimeSpan.FromSeconds(timeoutSeconds), cancellationToken).ConfigureAwait(false);
        var output = redactor.Redact(ChatProcessRunner.Truncate(
            $"exitCode: {result.ExitCode}\nstdout:\n{result.Stdout}\nstderr:\n{result.Stderr}"));
        return new ChatToolResult(JsonSerializer.Serialize(new
        {
            exitCode = result.ExitCode,
            timedOut = result.TimedOut,
            output,
        }));
    }

    private static string ErrorJson(string message) =>
        JsonSerializer.Serialize(new { error = message });
}

/// <summary>Process execution helper shared by the confined chat tools.</summary>
internal static class ChatProcessRunner
{
    public static async Task<(int ExitCode, string Stdout, string Stderr, bool TimedOut)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? stdin = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // SPEC-20261004 RF-006: stdin delivery for CLIs that read the
            // prompt from the pipe instead of argv.
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        WithoutHarnessEnv.Apply(startInfo.Environment, workingDirectory);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return (-1, string.Empty, $"failed to start {fileName}", false);
        }

        if (stdin is not null)
        {
            try
            {
                await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The CLI exited before reading — the exit code reports it.
            }
            process.StandardInput.Close();
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
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

        return (process.HasExited ? process.ExitCode : -1,
            Trim(await stdout.ConfigureAwait(false)),
            Trim(await stderr.ConfigureAwait(false)),
            timedOut);
    }

    public static string Truncate(string value) =>
        value.Length <= ShellExecTool.MaxOutputChars ? value : $"{value[..ShellExecTool.MaxOutputChars]}\n…(truncated)";

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already exited.
        }
    }

    private static string Trim(string value) => Truncate(value);
}
