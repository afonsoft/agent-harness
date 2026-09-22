using System.Diagnostics;
using Taskboard.Agents;
using Taskboard.Integrations.Execution;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Cliente ACP que executa o processo local do agente e captura stdout/stderr.
/// </summary>
public sealed class LocalCliAgentAcpClient : IAgentAcpClient
{
    private readonly IEnumerable<IAgentAdapter> _adapters;

    public LocalCliAgentAcpClient(IEnumerable<IAgentAdapter> adapters)
    {
        _adapters = adapters;
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        IProgress<AgentLogMessage> progress,
        CancellationToken cancellationToken = default)
    {
        var adapter = _adapters.FirstOrDefault(a => a.CanHandle(request.AgentType));
        if (adapter is null)
        {
            throw new NotSupportedException($"No adapter found for agent type '{request.AgentType}'.");
        }

        var command = adapter.BuildCommand(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            WorkingDirectory = command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        WithoutHarnessEnv.RemoveFrom(startInfo.Environment);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Failed to start agent process '{command.ExecutablePath}'.");
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                progress.Report(new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.StdOut, e.Data));
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                progress.Report(new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.StdErr, e.Data));
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignora falhas ao encerrar o processo já cancelado.
            }

            throw;
        }

        // SPEC-20260921-agent-execution-event-pipeline RF-004: telemetry —
        // wall-clock duration and the resolved model flow into run metrics.
        return new AgentExecutionResult(
            process.ExitCode,
            process.ExitCode == 0,
            Duration: stopwatch.Elapsed,
            ModelUsed: request.ResolvedModelName);
    }
}
