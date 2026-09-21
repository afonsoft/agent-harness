using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Integrations.Execution;
using Taskboard.ValueObjects;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Interactive ACP session client for CLI agents over JSON-RPC on stdio.
/// Handshake per SPEC-20260921-agent-execution-event-pipeline RF-002:
/// <c>initialize</c> → <c>session/new(cwd, mcpServers)</c> → <c>session/prompt(sessionId, content blocks)</c>;
/// agent requests (<c>session/request_permission</c>, <c>fs/*</c>, <c>terminal/*</c>)
/// get a JSON-RPC response on the original id.
/// </summary>
public sealed class AcpSessionClient : IAgentSessionClient, IDisposable
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);

    private sealed record PendingPermission(string JsonRpcId, IReadOnlyList<string> Options);

    /// <summary>Raised when the agent answers a JSON-RPC request with an error object.</summary>
    private sealed class AcpRequestException(string method, string error)
        : Exception($"ACP request '{method}' failed: {error}");

    private sealed class SessionHolder
    {
        public required string ThreadId { get; init; }
        public required Process Process { get; init; }
        public required StreamWriter Stdin { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        /// <summary>Serializes stdin writes — prompts, replies and error responses share the channel.</summary>
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public string? SessionId { get; set; }
        public ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> PendingResponses { get; } = new();
        public ConcurrentDictionary<string, PendingPermission> PendingPermissions { get; } = new();
    }

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
        string? modelName = null,
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

        var command = adapter.BuildSessionCommand(agentType, workspacePath, sandbox, modelName);

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
        var holder = new SessionHolder
        {
            ThreadId = threadId,
            Process = process,
            Stdin = process.StandardInput,
            Cts = cts
        };
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

        // RF-002: ACP handshake — initialize before session/new.
        var initResult = await SendRequestAsync(holder, "initialize", new
        {
            protocolVersion = 1,
            clientCapabilities = new
            {
                fs = new { readTextFile = false, writeTextFile = false },
                terminal = false
            }
        }, cancellationToken).ConfigureAwait(false);

        if (initResult is null)
        {
            EmitEvent(threadId, "error", "system",
                "Agent did not complete the ACP initialize handshake.", null);
            await StopSessionAsync(threadId, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var newResult = await SendRequestAsync(holder, "session/new", new
        {
            cwd = workspacePath,
            mcpServers = Array.Empty<object>()
        }, cancellationToken).ConfigureAwait(false);

        if (newResult is null)
        {
            EmitEvent(threadId, "error", "system",
                "Agent did not answer session/new.", null);
            await StopSessionAsync(threadId, cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (newResult.Value.TryGetProperty("sessionId", out var sid))
        {
            holder.SessionId = sid.GetString();
        }

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

        // Real ACP: session/prompt takes content blocks and only responds when
        // the turn ends (stopReason) — awaiting the response here would hold
        // the HTTP endpoint for the whole turn. The id stays registered and
        // the response becomes a session event in the dispatch loop.
        object promptParams = holder.SessionId is { } sessionId
            ? new { sessionId, prompt = new[] { new { type = "text", text } } }
            : (object)new { text, delivery };

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        holder.PendingResponses[id] = tcs;
        _ = tcs.Task.ContinueWith(t =>
        {
            holder.PendingResponses.TryRemove(id, out _);
            if (t is { IsCompletedSuccessfully: true, Status: TaskStatus.RanToCompletion })
            {
                var stopReason = t.Result.TryGetProperty("stopReason", out var sr)
                    ? sr.GetString() : null;
                EmitEvent(threadId, "session", "system", "Prompt turn completed",
                    JsonSerializer.Serialize(new { state = "ready", stopReason }));
            }
            else if (t.IsFaulted)
            {
                EmitEvent(threadId, "error", "system",
                    $"Prompt turn rejected: {t.Exception?.GetBaseException().Message}", null);
            }
        }, CancellationToken.None);

        try
        {
            await WriteLineAsync(holder, new { jsonrpc = "2.0", id, method = "session/prompt", @params = promptParams }, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            holder.PendingResponses.TryRemove(id, out _);
            tcs.TrySetCanceled();
            return false;
        }
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
                @params = holder.SessionId is { } sid ? (object)new { sessionId = sid } : new { }
            };

            await WriteLineAsync(holder, payload, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Fallback: kill the tree if stdio is broken.
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

        // Real ACP: the reply is a JSON-RPC response on the id of the
        // session/request_permission request — outcome.selected carries the
        // chosen optionId.
        if (holder.PendingPermissions.TryRemove(requestId, out var pending))
        {
            var optionId = MapOutcomeToOption(outcome, pending.Options);
            object result = optionId is null
                ? new { outcome = new { outcome = "cancelled" } }
                : new { outcome = new { outcome = "selected", optionId } };

            var response = new { jsonrpc = "2.0", id = pending.JsonRpcId, result };
            await WriteLineAsync(holder, response, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // Legacy shape: session/reply_permission method with params.requestId.
        var legacy = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N"),
            method = "session/reply_permission",
            @params = new { requestId, outcome }
        };

        await WriteLineAsync(holder, legacy, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Maps a PermissionGate outcome (allow/deny) to an optionId among the
    /// options offered by the agent; null → outcome "cancelled".
    /// </summary>
    internal static string? MapOutcomeToOption(string outcome, IReadOnlyList<string> options)
    {
        var normalized = outcome.ToLowerInvariant();
        var wanted = normalized switch
        {
            "allow" => "allow",
            "deny" => "reject",
            _ => normalized
        };

        return options.FirstOrDefault(o => o.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            ?? (normalized == "deny" ? null : options.FirstOrDefault());
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
            foreach (var pending in holder.PendingResponses.Values)
            {
                pending.TrySetCanceled();
            }
            TryKill(holder.Process);
            holder.Process.Dispose();
            holder.Cts.Dispose();
            EmitEvent(threadId, "session", "system", "Session stopped", "{\"state\":\"dead\"}");
        }

        return Task.CompletedTask;
    }

    private void HandleStdout(string threadId, string line)
    {
        if (!_sessions.TryGetValue(threadId, out var holder))
        {
            return;
        }

        try
        {
            var parsed = AcpProtocolParser.Parse(line);
            if (parsed is null)
            {
                EmitEvent(threadId, "message", "assistant", line, null);
                return;
            }

            switch (parsed.Type)
            {
                case AcpProtocolParser.MessageType.Response:
                    if (parsed.RequestId is { } rid
                        && holder.PendingResponses.TryRemove(rid, out var tcs))
                    {
                        if (parsed.ResponseResult.ValueKind == JsonValueKind.Undefined)
                        {
                            // Error response — fault the waiter so callers can
                            // distinguish rejection from a missing answer.
                            tcs.TrySetException(new AcpRequestException(
                                rid, parsed.ResponseError.GetRawText()));
                        }
                        else
                        {
                            tcs.TrySetResult(parsed.ResponseResult);
                        }
                    }
                    return;

                case AcpProtocolParser.MessageType.Request:
                    HandleAgentRequest(holder, parsed, line);
                    return;

                default:
                    EmitEvent(threadId, parsed.Kind, "assistant", parsed.Content, parsed.PayloadJson,
                        parsed.SessionId, parsed.ToolCallId);
                    return;
            }
        }
        catch
        {
            EmitEvent(threadId, "message", "assistant", line, null);
        }
    }

    private void HandleAgentRequest(SessionHolder holder, AcpProtocolParser.Parsed parsed, string rawLine)
    {
        if (parsed.Method == "session/request_permission" && parsed.RequestId is { } rpcId)
        {
            // Extract options from the normalized payload for the reply mapping.
            var options = ExtractPermissionOptions(parsed.PayloadJson);
            var effectiveId = ExtractPermissionRequestId(parsed.PayloadJson) ?? rpcId;
            holder.PendingPermissions[effectiveId] = new PendingPermission(rpcId, options);
            EmitEvent(holder.ThreadId, "permission", "assistant", parsed.Content, parsed.PayloadJson);
            return;
        }

        // Unsupported requests (fs/*, terminal/* — capabilities declared false):
        // answer Method not found so the agent does not hang.
        _ = Task.Run(async () =>
        {
            try
            {
                await WriteLineAsync(holder, new
                {
                    jsonrpc = "2.0",
                    id = parsed.RequestId,
                    error = new { code = -32601, message = $"Method '{parsed.Method}' not supported by this client." }
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // stdio failed — the session will die via the process watchdog.
            }
        });
        EmitEvent(holder.ThreadId, "activity", "system",
            $"Agent request '{parsed.Method}' not supported (declared capabilities).", rawLine);
    }

    private static IReadOnlyList<string> ExtractPermissionOptions(string? payloadJson)
    {
        if (payloadJson is null)
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
            {
                return opts.EnumerateArray().Select(o => o.GetString() ?? string.Empty)
                    .Where(s => s.Length > 0).ToList();
            }
        }
        catch (JsonException)
        {
        }

        return [];
    }

    private static string? ExtractPermissionRequestId(string? payloadJson)
    {
        if (payloadJson is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("requestId", out var r) ? r.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Sends a JSON-RPC request and awaits the response with the handshake timeout.</summary>
    private async Task<JsonElement?> SendRequestAsync(
        SessionHolder holder, string method, object @params, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        holder.PendingResponses[id] = tcs;

        try
        {
            await WriteLineAsync(holder, new { jsonrpc = "2.0", id, method, @params }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            holder.PendingResponses.TryRemove(id, out _);
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HandshakeTimeout);
        await using var reg = timeout.Token.Register(() => tcs.TrySetCanceled())
            .ConfigureAwait(false);

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (AcpRequestException ex)
        {
            // The agent answered with a JSON-RPC error — surface it so the UI
            // shows the rejection reason instead of a silent failure.
            EmitEvent(holder.ThreadId, "error", "system", ex.Message, null);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            holder.PendingResponses.TryRemove(id, out _);
        }
    }

    private static async Task WriteLineAsync(SessionHolder holder, object payload, CancellationToken cancellationToken)
    {
        await holder.WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await holder.Stdin.WriteLineAsync(JsonSerializer.Serialize(payload)).ConfigureAwait(false);
            await holder.Stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            holder.WriteLock.Release();
        }
    }

    private void EmitEvent(
        string threadId, string kind, string role, string? content, string? payload,
        string? sessionId = null, string? toolCallId = null)
    {
        var evt = new AgentSessionEvent(threadId, DateTimeOffset.UtcNow, kind, role, content, payload,
            SessionId: sessionId, ToolCallId: toolCallId);
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
            // Process already dead — nothing to do.
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
