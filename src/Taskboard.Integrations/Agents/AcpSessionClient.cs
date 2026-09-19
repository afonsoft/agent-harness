using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Integrations.Execution;
using Taskboard.ValueObjects;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Cliente ACP de sessão interativa com agentes CLI via JSON-RPC sobre stdio.
/// </summary>
public sealed class AcpSessionClient : IAgentSessionClient, IDisposable
{
    private sealed record SessionHolder(
        string ThreadId,
        Process Process,
        StreamWriter Stdin,
        CancellationTokenSource Cts);

    private readonly IEnumerable<IAgentAdapter> _adapters;
    private readonly ConcurrentDictionary<string, SessionHolder> _sessions = new();
    private readonly ConcurrentDictionary<string, Action<AgentSessionEvent>> _listeners = new();

    public AcpSessionClient(IEnumerable<IAgentAdapter> adapters)
    {
        _adapters = adapters;
    }

    public void RegisterEventListener(string threadId, Action<AgentSessionEvent> onEvent)
    {
        _listeners[threadId] = onEvent;
    }

    public void UnregisterEventListener(string threadId)
    {
        _listeners.TryRemove(threadId, out _);
    }

    public async Task<bool> StartSessionAsync(
        string threadId,
        AgentType agentType,
        string workspacePath,
        Sandbox sandbox,
        CancellationToken cancellationToken = default)
    {
        if (IsSessionActive(threadId))
        {
            return true;
        }

        var adapter = _adapters.FirstOrDefault(a => a.CanHandle(agentType));
        if (adapter is null)
        {
            throw new NotSupportedException($"No adapter found for agent type '{agentType}'.");
        }

        var command = adapter.BuildSessionCommand(agentType, workspacePath, sandbox);

        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            WorkingDirectory = command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in command.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        WithoutTaskboardEnv.RemoveFrom(startInfo.Environment);

        var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        var cts = new CancellationTokenSource();
        var holder = new SessionHolder(threadId, process, process.StandardInput, cts);
        _sessions[threadId] = holder;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                HandleStdout(threadId, e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                EmitEvent(threadId, "error", "assistant", e.Data, null);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Envia requisição inicial session/new
        var newSessionPayload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N"),
            method = "session/new",
            @params = new
            {
                cwd = workspacePath,
                sandboxMode = sandbox.Value
            }
        };

        await holder.Stdin.WriteLineAsync(JsonSerializer.Serialize(newSessionPayload)).ConfigureAwait(false);
        await holder.Stdin.FlushAsync(cancellationToken).ConfigureAwait(false);

        EmitEvent(threadId, "session", "system", "Session started", "{\"state\":\"ready\"}");
        return true;
    }

    public async Task<bool> SendPromptAsync(
        string threadId,
        string text,
        string delivery = "queue",
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || holder.Process.HasExited)
        {
            return false;
        }

        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N"),
            method = "session/prompt",
            @params = new
            {
                text,
                delivery
            }
        };

        await holder.Stdin.WriteLineAsync(JsonSerializer.Serialize(payload)).ConfigureAwait(false);
        await holder.Stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> CancelAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || holder.Process.HasExited)
        {
            return false;
        }

        try
        {
            var payload = new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString("N"),
                method = "session/cancel",
                @params = new { }
            };

            await holder.Stdin.WriteLineAsync(JsonSerializer.Serialize(payload)).ConfigureAwait(false);
            await holder.Stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Fallback: kill da árvore se stdio falhar
            TryKill(holder.Process);
        }

        return true;
    }

    public async Task<bool> ReplyPermissionAsync(
        string threadId,
        string requestId,
        string outcome,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || holder.Process.HasExited)
        {
            return false;
        }

        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N"),
            method = "session/reply_permission",
            @params = new
            {
                requestId,
                outcome
            }
        };

        await holder.Stdin.WriteLineAsync(JsonSerializer.Serialize(payload)).ConfigureAwait(false);
        await holder.Stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public bool IsSessionActive(string threadId)
    {
        if (_sessions.TryGetValue(threadId, out var holder))
        {
            if (!holder.Process.HasExited)
            {
                return true;
            }

            _sessions.TryRemove(threadId, out _);
        }

        return false;
    }

    public Task StopSessionAsync(string threadId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(threadId, out var holder))
        {
            holder.Cts.Cancel();
            TryKill(holder.Process);
            holder.Process.Dispose();
            holder.Cts.Dispose();
            EmitEvent(threadId, "session", "system", "Session stopped", "{\"state\":\"dead\"}");
        }

        return Task.CompletedTask;
    }

    private void HandleStdout(string threadId, string line)
    {
        try
        {
            if (line.TrimStart().StartsWith('{'))
            {
                var (method, kind, content, payload) = AcpSessionMessageParser.ParseNotification(line);
                if (method == "session/request_permission")
                {
                    EmitEvent(threadId, "permission", "assistant", content, payload);
                    return;
                }

                EmitEvent(threadId, kind, "assistant", content, payload);
                return;
            }

            // Linha não-JSON (stdout raw)
            EmitEvent(threadId, "message", "assistant", line, null);
        }
        catch
        {
            EmitEvent(threadId, "message", "assistant", line, null);
        }
    }

    private void EmitEvent(string threadId, string kind, string role, string content, string? payload)
    {
        var evt = new AgentSessionEvent(threadId, DateTimeOffset.UtcNow, kind, role, content, payload);
        if (_listeners.TryGetValue(threadId, out var listener))
        {
            listener(evt);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignora se o processo já morreu
        }
    }

    public void Dispose()
    {
        foreach (var key in _sessions.Keys.ToList())
        {
            _ = StopSessionAsync(key);
        }
    }
}
