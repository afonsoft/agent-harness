using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Integrations.Execution;
using Taskboard.ValueObjects;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// Interactive ACP v1 session client for CLI agents over JSON-RPC stdio (or TCP).
/// SPEC-20260921-acp-v1-conformance: real handshake with capability capture,
/// spec-correct cancel (notification + cancelled permission replies),
/// session lifecycle (new/resume/load/close), config options, auth flow,
/// client-side fs/terminal dispatch, watchdog + pending drain, timeouts.
/// </summary>
public sealed class AcpSessionClient : IAgentSessionClient, IDisposable
{
    internal sealed record AcpPermissionOption(string OptionId, string? Name, string? Kind);

    private sealed record PendingPermission(string JsonRpcId, IReadOnlyList<AcpPermissionOption> Options);

    /// <summary>State a session needs to respawn (reconnect after process death).</summary>
    private sealed record SpawnContext(AgentType AgentType, string WorkspacePath, Sandbox Sandbox, string? ModelName);

    private sealed class SessionHolder
    {
        public required string ThreadId { get; init; }
        public required SpawnContext Spawn { get; init; }
        public Process? Process { get; set; }
        public TcpClient? Socket { get; set; }
        public required StreamWriter Writer { get; init; }
        public required StreamReader Reader { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        /// <summary>Serializes writes — prompts, replies and error responses share the channel.</summary>
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public string? SessionId { get; set; }
        public AcpPeerInfo Peer { get; set; } = new();
        public ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> PendingResponses { get; } = new();
        public ConcurrentDictionary<string, PendingPermission> PendingPermissions { get; } = new();
        /// <summary>Registration that enforces TurnTimeout on the in-flight prompt.</summary>
        public CancellationTokenRegistration TurnTimeoutReg { get; set; }
        /// <summary>True while the session is expected to stay alive (drives auto-reconnect).</summary>
        public bool WantsReconnect { get; set; } = true;
    }

    private readonly IEnumerable<IAgentAdapter> _adapters;
    private readonly AcpSessionOptions _options;
    private readonly IAcpClientToolHandler? _toolHandler;
    private readonly ConcurrentDictionary<string, SessionHolder> _sessions = new();
    private readonly ConcurrentDictionary<string, Action<AgentSessionEvent>> _listeners = new();

    /// <summary>Last known session id per thread — enables session/resume on reconnect.</summary>
    private readonly ConcurrentDictionary<string, string> _lastSessionIds = new();

    public AcpSessionClient(
        IEnumerable<IAgentAdapter> adapters,
        AcpSessionOptions? options = null,
        IAcpClientToolHandler? toolHandler = null)
    {
        _adapters = adapters;
        _options = options ?? new AcpSessionOptions();
        _toolHandler = toolHandler;
    }

    public void RegisterEventListener(string threadId, Action<AgentSessionEvent> onEvent)
    {
        _listeners[threadId] = onEvent;
    }

    public void UnregisterEventListener(string threadId)
    {
        _listeners.TryRemove(threadId, out _);
    }

    /// <summary>Capabilities/config negotiated for the thread's live session, if any.</summary>
    public AcpPeerInfo? GetPeerInfo(string threadId) =>
        _sessions.TryGetValue(threadId, out var holder) ? holder.Peer : null;

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

        var spawn = new SpawnContext(agentType, workspacePath, sandbox, modelName);
        var holder = await SpawnChannelAsync(threadId, spawn, adapter, cancellationToken).ConfigureAwait(false);
        if (holder is null)
        {
            return false;
        }

        _sessions[threadId] = holder;
        _ = Task.Run(() => ReadLoopAsync(holder));

        // RF-001: ACP initialize — clientInfo + real clientCapabilities; the
        // response carries the negotiated version, agentCapabilities and
        // authMethods, all captured into holder.Peer.
        var initResult = await SendRequestAsync(holder, "initialize", new
        {
            protocolVersion = 1,
            clientCapabilities = BuildClientCapabilities(),
            clientInfo = new { name = "taskboard", title = "Harness", version = "1.0.0" }
        }, _options.HandshakeTimeout, cancellationToken).ConfigureAwait(false);

        if (initResult is null)
        {
            EmitEvent(threadId, "error", "system",
                "Agent did not complete the ACP initialize handshake.", null);
            await StopSessionAsync(threadId, cancellationToken).ConfigureAwait(false);
            return false;
        }

        holder.Peer = AcpPeerInfo.FromInitialize(initResult.Value);

        if (holder.Peer.ProtocolVersion != 1)
        {
            EmitEvent(threadId, "error", "system",
                $"Agent answered protocolVersion {holder.Peer.ProtocolVersion}; this client speaks ACP v1.", null);
            await StopSessionAsync(threadId, cancellationToken).ConfigureAwait(false);
            return false;
        }

        EmitPeerInfo(holder);

        // RF-004: authMethods advertised → authenticate before session/new.
        if (!await AuthenticateIfRequiredAsync(holder, cancellationToken).ConfigureAwait(false))
        {
            await StopSessionAsync(threadId, cancellationToken).ConfigureAwait(false);
            return false;
        }

        // RF-002: prefer resuming the previous agent session when the agent
        // supports it — keeps CLI-side context across process restarts.
        var resumed = await TryResumeSessionAsync(holder, cancellationToken).ConfigureAwait(false);
        if (!resumed)
        {
            var newResult = await SendRequestAsync(holder, "session/new", BuildSessionNewParams(holder, workspacePath),
                _options.RequestTimeout, cancellationToken).ConfigureAwait(false);

            if (newResult is null)
            {
                EmitEvent(threadId, "error", "system", "Agent did not answer session/new.", null);
                await StopSessionAsync(threadId, cancellationToken).ConfigureAwait(false);
                return false;
            }

            if (newResult.Value.TryGetProperty("sessionId", out var sid))
            {
                holder.SessionId = sid.GetString();
            }

            holder.Peer.ApplySessionResult(newResult.Value);
        }

        if (holder.SessionId is not null)
        {
            _lastSessionIds[threadId] = holder.SessionId;
            holder.Peer.SessionId = holder.SessionId;
        }

        EmitEvent(threadId, "session", "system", "Session started",
            JsonSerializer.Serialize(new { state = "ready", sessionId = holder.SessionId, resumed }));
        EmitSessionInfo(holder);
        return true;
    }

    private object BuildClientCapabilities() => new
    {
        fs = new { readTextFile = _options.ClientFs, writeTextFile = _options.ClientFs },
        terminal = _options.ClientTerminal,
        auth = new { terminal = _options.TerminalAuth },
        session = new { configOptions = new { boolean = _options.BooleanConfigOptions ? new { } : (object?)null } }
    };

    private object BuildSessionNewParams(SessionHolder holder, string workspacePath)
    {
        var mcpServers = BuildMcpServers(holder.Peer);
        if (holder.Peer.AdditionalDirectories)
        {
            return new { cwd = workspacePath, mcpServers, additionalDirectories = Array.Empty<string>() };
        }

        return new { cwd = workspacePath, mcpServers };
    }

    /// <summary>RF-012: configured MCP servers (e.g. RAG/Knowledge) handed to the agent.</summary>
    private object[] BuildMcpServers(AcpPeerInfo peer)
    {
        var list = new List<object>();
        foreach (var server in _options.McpServers)
        {
            if (!string.IsNullOrWhiteSpace(server.Url))
            {
                if (!peer.McpHttp)
                {
                    continue; // v1: http transport requires mcpCapabilities.http
                }

                var headers = server.Headers is { Count: > 0 }
                    ? server.Headers.Select(h => new { name = h.Key, value = h.Value }).ToArray()
                    : Array.Empty<object>();
                list.Add(new { type = "http", name = server.Name, url = server.Url, headers });
            }
            else if (!string.IsNullOrWhiteSpace(server.Command))
            {
                list.Add(new
                {
                    name = server.Name,
                    command = server.Command,
                    args = server.Args,
                    env = Array.Empty<object>()
                });
            }
        }

        return list.ToArray();
    }

    private async Task<bool> AuthenticateIfRequiredAsync(SessionHolder holder, CancellationToken cancellationToken)
    {
        if (holder.Peer.AuthMethods.Count == 0)
        {
            return true;
        }

        var agentMethod = holder.Peer.AuthMethods
            .FirstOrDefault(m => m.Type is "agent" or "" || string.IsNullOrEmpty(m.Type));
        if (agentMethod is null)
        {
            // terminal-type methods need an interactive login outside the ACP channel.
            var terminal = holder.Peer.AuthMethods.First();
            EmitEvent(holder.ThreadId, "error", "system",
                $"Agent requires interactive login — run the agent CLI in a terminal ({terminal.Name}).",
                JsonSerializer.Serialize(new { code = "auth_required", method = terminal.Id, args = terminal.Args }));
            return false;
        }

        var auth = await SendRequestAsync(holder, "authenticate", new { methodId = agentMethod.Id },
            _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
        if (auth is null)
        {
            EmitEvent(holder.ThreadId, "error", "system",
                "Agent authentication failed.", JsonSerializer.Serialize(new { code = "auth_required" }));
            return false;
        }

        EmitEvent(holder.ThreadId, "lifecycle", "system",
            $"Authenticated via '{agentMethod.Id}'.", null);
        return true;
    }

    private async Task<bool> TryResumeSessionAsync(SessionHolder holder, CancellationToken cancellationToken)
    {
        if (!_lastSessionIds.TryGetValue(holder.ThreadId, out var previousId))
        {
            return false;
        }

        object p = new
        {
            sessionId = previousId,
            cwd = holder.Spawn.WorkspacePath,
            mcpServers = BuildMcpServers(holder.Peer)
        };

        JsonElement? result = null;
        if (holder.Peer.SessionResume)
        {
            result = await SendRequestAsync(holder, "session/resume", p, _options.RequestTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (holder.Peer.LoadSession)
        {
            // session/load replays history as session/update notifications before responding.
            result = await SendRequestAsync(holder, "session/load", p, _options.RequestTimeout * 4, cancellationToken)
                .ConfigureAwait(false);
        }

        if (result is null)
        {
            return false;
        }

        holder.SessionId = previousId;
        holder.Peer.ApplySessionResult(result.Value);
        return true;
    }

    public async Task<bool> SendPromptAsync(
        string threadId,
        string text,
        string delivery = "queue",
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || !IsAlive(holder))
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

        // RF-011: a stuck agent must not leak the pending turn forever.
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(holder.Cts.Token);
        timeoutCts.CancelAfter(_options.TurnTimeout);
        holder.TurnTimeoutReg = timeoutCts.Token.Register(() =>
        {
            if (holder.PendingResponses.TryRemove(id, out var pending))
            {
                pending.TrySetException(new AcpException(AcpErrorCode.TurnTimeout, "session/prompt",
                    $"turn exceeded {_options.TurnTimeout}"));
            }
        });

        _ = tcs.Task.ContinueWith(t =>
        {
            holder.TurnTimeoutReg.Dispose();
            timeoutCts.Dispose();
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

    /// <summary>RF-003: set a session config option (model, mode, thought_level…).</summary>
    public async Task<bool> SetConfigOptionAsync(
        string threadId, string configId, string value, bool isBoolean = false,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || !IsAlive(holder) || holder.SessionId is null)
        {
            return false;
        }

        object p = isBoolean
            ? new { sessionId = holder.SessionId, configId, type = "boolean", value = bool.Parse(value) }
            : new { sessionId = holder.SessionId, configId, value };

        var result = await SendRequestAsync(holder, "session/set_config_option", p,
            _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return false;
        }

        holder.Peer.ApplySessionResult(result.Value);
        EmitSessionInfo(holder);
        return true;
    }

    /// <summary>RF-003: legacy mode switching for agents that only expose modes.</summary>
    public async Task<bool> SetModeAsync(string threadId, string modeId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || !IsAlive(holder) || holder.SessionId is null)
        {
            return false;
        }

        var result = await SendRequestAsync(holder, "session/set_mode",
            new { sessionId = holder.SessionId, modeId },
            _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return false;
        }

        EmitSessionInfo(holder);
        return true;
    }

    public async Task<bool> CancelAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || !IsAlive(holder))
        {
            return false;
        }

        try
        {
            // RF-005: session/cancel is a notification — no id, no response expected.
            var payload = holder.SessionId is { } sid
                ? (object)new { jsonrpc = "2.0", method = "session/cancel", @params = new { sessionId = sid } }
                : new { jsonrpc = "2.0", method = "session/cancel", @params = new { } };

            await WriteLineAsync(holder, payload, cancellationToken).ConfigureAwait(false);

            // The spec requires answering every pending permission request with
            // the cancelled outcome so the agent can unwind its turn.
            foreach (var (requestId, pending) in holder.PendingPermissions.ToArray())
            {
                if (holder.PendingPermissions.TryRemove(requestId, out _))
                {
                    await WriteLineAsync(holder, new
                    {
                        jsonrpc = "2.0",
                        id = pending.JsonRpcId,
                        result = new { outcome = new { outcome = "cancelled" } }
                    }, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // Fallback: kill the tree if stdio is broken.
            TryKill(holder);
        }

        return true;
    }

    public async Task<bool> ReplyPermissionAsync(
        string threadId,
        string requestId,
        string outcome,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || !IsAlive(holder))
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
    /// Maps a PermissionGate outcome (allow/deny/always) to an optionId among the
    /// options offered by the agent, preferring the ACP permission option
    /// <c>kind</c> over substring matching; null → outcome "cancelled".
    /// </summary>
    internal static string? MapOutcomeToOption(string outcome, IReadOnlyList<AcpPermissionOption> options)
    {
        var normalized = outcome.ToLowerInvariant();
        var wanted = normalized switch
        {
            "allow" => new[] { "allow_once", "allow_always" },
            "always" => new[] { "allow_always", "allow_once" },
            "deny" => new[] { "reject_once", "reject_always" },
            _ => new[] { normalized }
        };

        foreach (var kind in wanted)
        {
            var byKind = options.FirstOrDefault(o =>
                string.Equals(o.Kind, kind, StringComparison.OrdinalIgnoreCase));
            if (byKind is not null)
            {
                return byKind.OptionId;
            }
        }

        // Fallback for agents that don't tag kinds: match by id/name substring.
        var probe = normalized == "deny" ? "reject" : normalized == "always" ? "allow" : normalized;
        return options.FirstOrDefault(o =>
                (o.OptionId.Contains(probe, StringComparison.OrdinalIgnoreCase))
                || (o.Name?.Contains(probe, StringComparison.OrdinalIgnoreCase) ?? false)
                || (normalized == "deny" && (o.Name?.Contains("deny", StringComparison.OrdinalIgnoreCase) ?? false)))
            ?.OptionId
            ?? (normalized == "deny" ? null : options.FirstOrDefault()?.OptionId);
    }

    public bool IsSessionActive(string threadId)
    {
        if (_sessions.TryGetValue(threadId, out var holder))
        {
            if (IsAlive(holder))
            {
                return true;
            }

            _sessions.TryRemove(threadId, out _);
        }

        return false;
    }

    private static bool IsAlive(SessionHolder holder) =>
        holder.Socket?.Connected ?? holder.Process is { HasExited: false };

    public async Task StopSessionAsync(string threadId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(threadId, out var holder))
        {
            holder.WantsReconnect = false;
            holder.Cts.Cancel();

            // RF-002: graceful session/close before killing when the agent supports it.
            if (holder.SessionId is not null && holder.Peer.SessionClose && IsAlive(holder))
            {
                try
                {
                    await WriteLineAsync(holder, new
                    {
                        jsonrpc = "2.0",
                        id = Guid.NewGuid().ToString("N"),
                        method = "session/close",
                        @params = new { sessionId = holder.SessionId }
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Channel already broken — fall through to kill.
                }
            }

            foreach (var pending in holder.PendingResponses.Values)
            {
                pending.TrySetCanceled();
            }

            TryKill(holder);
            holder.Process?.Dispose();
            holder.Socket?.Dispose();
            holder.Cts.Dispose();
            EmitEvent(threadId, "session", "system", "Session stopped", "{\"state\":\"dead\"}");
        }
    }

    /// <summary>True when the last session died unexpectedly and the agent can resume it.</summary>
    public bool CanResume(string threadId) =>
        _lastSessionIds.ContainsKey(threadId);

    private async Task ReadLoopAsync(SessionHolder holder)
    {
        try
        {
            while (!holder.Cts.IsCancellationRequested)
            {
                var line = await holder.Reader.ReadLineAsync(holder.Cts.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break; // EOF — process exited or socket closed.
                }

                HandleStdout(holder.ThreadId, line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            EmitEvent(holder.ThreadId, "error", "system", $"Agent channel read failed: {ex.Message}", null);
        }
        finally
        {
            OnChannelClosed(holder);
        }
    }

    /// <summary>RF-011: drain pending requests and report the exit once the channel dies.</summary>
    private void OnChannelClosed(SessionHolder holder)
    {
        if (!_sessions.TryGetValue(holder.ThreadId, out var current) || !ReferenceEquals(current, holder))
        {
            return; // StopSessionAsync already owns the teardown.
        }

        _sessions.TryRemove(holder.ThreadId, out _);

        var exitCode = holder.Process is { HasExited: true } ? holder.Process.ExitCode : (int?)null;
        foreach (var pending in holder.PendingResponses.Values)
        {
            pending.TrySetException(new AcpException(AcpErrorCode.ProcessDied, "channel",
                "agent process exited"));
        }

        holder.PendingPermissions.Clear();
        EmitEvent(holder.ThreadId, "lifecycle", "system", "Agent process exited",
            JsonSerializer.Serialize(new { state = "dead", exitCode, sessionId = holder.SessionId }));
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
                            tcs.TrySetException(AcpException.FromErrorElement(rid, parsed.ResponseError));
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

                case AcpProtocolParser.MessageType.Notification
                    when parsed.Method == "$/cancel_request":
                    HandleAgentCancelRequest(holder, parsed);
                    return;

                default:
                    if (parsed.Kind == "session_info" || parsed.Kind == "commands")
                    {
                        UpdatePeerFromUpdate(holder, parsed);
                    }

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

    /// <summary>Keep holder.Peer in sync with mode/config/info session updates.</summary>
    private static void UpdatePeerFromUpdate(SessionHolder holder, AcpProtocolParser.Parsed parsed)
    {
        if (parsed.PayloadJson is null)
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(parsed.PayloadJson);
            var update = doc.RootElement;
            if (update.TryGetProperty("sessionUpdate", out var su))
            {
                switch (su.GetString())
                {
                    case "config_option_update" when update.TryGetProperty("configOptions", out var co):
                        holder.Peer.ConfigOptions = co.Clone();
                        break;
                    case "current_mode_update" when holder.Peer.Modes is { } modes:
                        var obj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modes.GetRawText());
                        if (obj is not null && update.TryGetProperty("currentModeId", out var cm))
                        {
                            obj["currentModeId"] = cm.Clone();
                            holder.Peer.Modes = JsonSerializer.SerializeToElement(obj);
                        }
                        break;
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private void HandleAgentRequest(SessionHolder holder, AcpProtocolParser.Parsed parsed, string rawLine)
    {
        if (parsed.Method == "session/request_permission" && parsed.RequestId is { } rpcId)
        {
            var options = ExtractPermissionOptions(parsed.PayloadJson, rawLine);
            var effectiveId = ExtractPermissionRequestId(parsed.PayloadJson) ?? rpcId;
            holder.PendingPermissions[effectiveId] = new PendingPermission(rpcId, options);
            EmitEvent(holder.ThreadId, "permission", "assistant", parsed.Content, parsed.PayloadJson);
            return;
        }

        // RF-008/009: fs/*, terminal/*, elicitation/* and extension methods go to
        // the client tool handler when one is registered; otherwise -32601 so
        // the agent does not hang on an undeclared capability.
        _ = Task.Run(async () =>
        {
            try
            {
                object response;
                if (_toolHandler is not null && parsed.RequestId is not null && parsed.Params.ValueKind != JsonValueKind.Undefined)
                {
                    try
                    {
                        var result = await _toolHandler.HandleAsync(
                            holder.ThreadId, holder.SessionId ?? string.Empty,
                            parsed.Method, parsed.Params, holder.Cts.Token).ConfigureAwait(false);
                        response = new { jsonrpc = "2.0", id = parsed.RequestId, result };
                    }
                    catch (AcpException aex)
                    {
                        response = new { jsonrpc = "2.0", id = parsed.RequestId, error = new { code = ToJsonRpcCode(aex.Code), message = aex.Message } };
                    }
                }
                else
                {
                    response = new
                    {
                        jsonrpc = "2.0",
                        id = parsed.RequestId,
                        error = new { code = -32601, message = $"Method '{parsed.Method}' not supported by this client." }
                    };
                }

                await WriteLineAsync(holder, response, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Channel failed — the session will die via the read loop.
            }
        });
        EmitEvent(holder.ThreadId, "activity", "system",
            $"Agent request '{parsed.Method}'.", rawLine);
    }

    /// <summary>
    /// RF-005: <c>$/cancel_request</c> — the agent cancelled a request it sent
    /// us (permission, fs/*, terminal/*). Drop the pending permission keyed by
    /// that JSON-RPC id so it can no longer be answered; no response is sent
    /// (the message is a notification).
    /// </summary>
    private static void HandleAgentCancelRequest(SessionHolder holder, AcpProtocolParser.Parsed parsed)
    {
        if (parsed.Params.ValueKind != JsonValueKind.Object
            || !parsed.Params.TryGetProperty("id", out var idEl))
        {
            return;
        }

        var id = idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : idEl.GetRawText();
        foreach (var (key, pending) in holder.PendingPermissions)
        {
            if (pending.JsonRpcId == id)
            {
                holder.PendingPermissions.TryRemove(key, out _);
            }
        }
    }

    private static int ToJsonRpcCode(AcpErrorCode code) => code switch
    {
        AcpErrorCode.InvalidParams => -32602,
        AcpErrorCode.MethodNotFound => -32601,
        AcpErrorCode.RequestCancelled => -32800,
        _ => -32603,
    };

    private static IReadOnlyList<AcpPermissionOption> ExtractPermissionOptions(string? payloadJson, string rawLine)
    {
        // The normalized payload carries plain option ids; the raw line keeps
        // the real ACP objects {optionId,name,kind} — parse the raw line first.
        try
        {
            using var doc = JsonDocument.Parse(rawLine);
            if (doc.RootElement.TryGetProperty("params", out var p)
                && p.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
            {
                var list = new List<AcpPermissionOption>();
                foreach (var o in opts.EnumerateArray())
                {
                    if (o.ValueKind == JsonValueKind.Object
                        && o.TryGetProperty("optionId", out var oid) && oid.GetString() is { } id)
                    {
                        list.Add(new AcpPermissionOption(
                            id,
                            o.TryGetProperty("name", out var n) ? n.GetString() : null,
                            o.TryGetProperty("kind", out var k) ? k.GetString() : null));
                    }
                    else if (o.ValueKind == JsonValueKind.String && o.GetString() is { } legacy)
                    {
                        list.Add(new AcpPermissionOption(legacy, legacy, null));
                    }
                }

                if (list.Count > 0)
                {
                    return list;
                }
            }
        }
        catch (JsonException)
        {
        }

        return [new AcpPermissionOption("allow", "Allow", "allow_once"), new AcpPermissionOption("deny", "Deny", "reject_once")];
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

    /// <summary>Sends a JSON-RPC request and awaits the response within the timeout.</summary>
    private async Task<JsonElement?> SendRequestAsync(
        SessionHolder holder, string method, object @params, TimeSpan timeout, CancellationToken cancellationToken)
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

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, holder.Cts.Token);
        timeoutCts.CancelAfter(timeout);
        await using var reg = timeoutCts.Token.Register(() => tcs.TrySetCanceled())
            .ConfigureAwait(false);

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (AcpException ex)
        {
            // The agent answered with a JSON-RPC error — surface it so the UI
            // shows the rejection reason instead of a silent failure.
            EmitEvent(holder.ThreadId, "error", "system", ex.Message,
                JsonSerializer.Serialize(new { code = ex.Code.ToString() }));
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

    private async Task<SessionHolder?> SpawnChannelAsync(
        string threadId, SpawnContext spawn, IAgentAdapter adapter, CancellationToken cancellationToken)
    {
        var command = adapter.BuildSessionCommand(spawn.AgentType, spawn.WorkspacePath, spawn.Sandbox, spawn.ModelName);

        // RF-014: TCP when the adapter asks for it or Taskboard:Acp:TcpPort
        // points at an already-running ACP server.
        if ((command.TcpPort ?? _options.AgentTcpPort) is { } port)
        {
            // RF-014: TCP transport — connect to an already-running ACP server
            // (e.g. `copilot --acp --port`) instead of spawning a subprocess.
            var socket = new TcpClient();
            try
            {
                await socket.ConnectAsync(System.Net.IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                socket.Dispose();
                EmitEvent(threadId, "error", "system", $"Could not connect to agent ACP server on 127.0.0.1:{port}.", null);
                return null;
            }

            var stream = socket.GetStream();
            return new SessionHolder
            {
                ThreadId = threadId,
                Spawn = spawn,
                Socket = socket,
                Writer = new StreamWriter(stream) { AutoFlush = true },
                Reader = new StreamReader(stream),
                Cts = new CancellationTokenSource()
            };
        }

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
            return null;
        }

        var holder = new SessionHolder
        {
            ThreadId = threadId,
            Spawn = spawn,
            Process = process,
            Writer = process.StandardInput,
            Reader = process.StandardOutput,
            Cts = new CancellationTokenSource()
        };

        _ = Task.Run(async () =>
        {
            // stderr is agent-side logging per spec — informational, not errors.
            try
            {
                while (!holder.Cts.IsCancellationRequested)
                {
                    var err = await process.StandardError.ReadLineAsync(holder.Cts.Token).ConfigureAwait(false);
                    if (err is null)
                    {
                        break;
                    }

                    EmitEvent(threadId, "output", "assistant", err, null);
                }
            }
            catch (Exception)
            {
                // stderr closed/cancelled — read loop on stdout reports the death.
            }
        });

        return holder;
    }

    private void EmitPeerInfo(SessionHolder holder)
    {
        EmitEvent(holder.ThreadId, "session_info", "system",
            $"Connected to {holder.Peer.AgentName ?? "agent"} {holder.Peer.AgentVersion}".Trim(),
            JsonSerializer.Serialize(new
            {
                protocolVersion = holder.Peer.ProtocolVersion,
                agent = holder.Peer.AgentName,
                agentVersion = holder.Peer.AgentVersion,
                capabilities = new
                {
                    loadSession = holder.Peer.LoadSession,
                    resume = holder.Peer.SessionResume,
                    close = holder.Peer.SessionClose,
                    delete = holder.Peer.SessionDelete,
                    list = holder.Peer.SessionList,
                    additionalDirectories = holder.Peer.AdditionalDirectories,
                    mcpHttp = holder.Peer.McpHttp,
                    promptImage = holder.Peer.PromptImage,
                    authLogout = holder.Peer.AuthLogout
                },
                authMethods = holder.Peer.AuthMethods.Select(m => new { m.Id, m.Type, m.Name })
            }));
    }

    private void EmitSessionInfo(SessionHolder holder)
    {
        EmitEvent(holder.ThreadId, "session_info", "system", "Session configuration",
            JsonSerializer.Serialize(new
            {
                sessionId = holder.SessionId,
                modes = holder.Peer.Modes,
                configOptions = holder.Peer.ConfigOptions
            }));
    }

    private static async Task WriteLineAsync(SessionHolder holder, object payload, CancellationToken cancellationToken)
    {
        await holder.WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await holder.Writer.WriteLineAsync(JsonSerializer.Serialize(payload)).ConfigureAwait(false);
            await holder.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
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

    private static void TryKill(SessionHolder holder)
    {
        try
        {
            if (holder.Process is { HasExited: false })
            {
                holder.Process.Kill(entireProcessTree: true);
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
