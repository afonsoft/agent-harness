using System.Diagnostics;
using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Integrations.Execution;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Cliente ACP que troca mensagens JSON-RPC bidirecionais sobre stdin/stdout.
/// </summary>
public sealed class JsonRpcAcpClient : IAgentAcpClient
{
    private readonly IEnumerable<IAgentAdapter> _adapters;

    public JsonRpcAcpClient(IEnumerable<IAgentAdapter> adapters)
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

        var tcs = new TaskCompletionSource<AgentExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            WorkingDirectory = command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        WithoutTaskboardEnv.RemoveFrom(startInfo.Environment);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Failed to start agent process '{command.ExecutablePath}'.");
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                TryHandleOutput(e.Data, request.IssueId, tcs, progress);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                progress?.Report(new AgentLogMessage(DateTimeOffset.UtcNow, request.IssueId, AgentLogStream.StdErr, e.Data));
            }
        };

        using var _ = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignora se o processo já encerrou.
            }
        });

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        if (tcs.Task.IsCompleted)
        {
            return await tcs.Task.ConfigureAwait(false);
        }

        return new AgentExecutionResult(process.ExitCode, process.ExitCode == 0);
    }

    private static void TryHandleOutput(string line, string expectedId, TaskCompletionSource<AgentExecutionResult> tcs, IProgress<AgentLogMessage> progress)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!root.TryGetProperty("jsonrpc", out _))
            {
                progress?.Report(new AgentLogMessage(DateTimeOffset.UtcNow, expectedId, AgentLogStream.System, line));
                return;
            }

            if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
            {
                var id = idElement.GetString();
                if (id == expectedId)
                {
                    if (root.TryGetProperty("result", out var result))
                    {
                        var exitCode = TryGetExitCode(result);
                        tcs.TrySetResult(new AgentExecutionResult(exitCode, exitCode == 0));
                    }
                    else if (root.TryGetProperty("error", out _))
                    {
                        tcs.TrySetResult(new AgentExecutionResult(1, false));
                    }
                }
                else
                {
                    ReportNotification(line, expectedId, progress);
                }
            }
            else
            {
                ReportNotification(line, expectedId, progress);
            }
        }
        catch (JsonException)
        {
            progress?.Report(new AgentLogMessage(DateTimeOffset.UtcNow, expectedId, AgentLogStream.System, line));
        }
    }

    /// <summary>
    /// Notificação JSON-RPC fora do id de resultado: notificações
    /// <c>session/update</c> são normalizadas pelo parser ACP (tool_call, plan,
    /// thought…); demais viram texto <c>method: params</c> como antes.
    /// SPEC-20260921-agent-execution-event-pipeline RF-002.
    /// </summary>
    private static void ReportNotification(string line, string issueId, IProgress<AgentLogMessage> progress)
    {
        var parsed = AcpProtocolParser.Parse(line);
        if (parsed is not null
            && parsed.Method is "session/update" or "session/request_permission")
        {
            progress?.Report(new AgentLogMessage(
                DateTimeOffset.UtcNow,
                issueId,
                AgentLogStream.System,
                parsed.Content ?? parsed.Method,
                parsed.Kind,
                parsed.PayloadJson));
            return;
        }

        var content = parsed is { Method.Length: > 0 }
            ? $"{parsed.Method}: {GetParamsText(line)}"
            : line;
        progress?.Report(new AgentLogMessage(DateTimeOffset.UtcNow, issueId, AgentLogStream.System, content));
    }

    private static string GetParamsText(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("params", out var p) ? p.GetRawText() : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static int TryGetExitCode(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Number)
        {
            return result.GetInt32();
        }

        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("exitCode", out var exitCodeElement))
        {
            return exitCodeElement.GetInt32();
        }

        return 0;
    }

    private static string GetParamsText(JsonElement root)
    {
        return root.TryGetProperty("params", out var paramsElement) ? paramsElement.GetRawText() : string.Empty;
    }
}
