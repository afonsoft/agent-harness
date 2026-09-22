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
/// Interactive ACP session client for CLI agents over JSON-RPC stdio (or TCP).
/// SPEC-20260921-acp-v1-conformance: real handshake with capability capture,
/// spec-correct cancel (notification + cancelled permission replies),
/// session lifecycle (new/resume/load/close), config options, auth flow,
/// client-side fs/terminal dispatch, watchdog + pending drain, timeouts.
/// SPEC-20260921-acp-v2-readiness: the negotiated <c>protocolVersion</c>
/// selects an <see cref="IAcpDialect"/> per connection — v1 remains the
/// default, v2 is opt-in via Taskboard:Acp:MaxProtocolVersion.
/// </summary>
public sealed class AcpSessionClient : IAgentSessionClient, IDisposable
{
    internal sealed record AcpPermissionOption(string OptionId, string? Name, string? Kind);

    private sealed record PendingPermission(
        string JsonRpcId,
        IReadOnlyList<AcpPermissionOption> Options,
        CancellationTokenSource TimeoutCts,
        string RawLine);

    /// <summary>State a session needs to respawn (reconnect after process death).</summary>
    private sealed record SpawnContext(AgentType AgentType, string WorkspacePath, Sandbox Sandbox, string? ModelName);

    /// <summary>An in-flight prompt turn — ends on the RPC response (v1) or a state_update (v2).</summary>
    private sealed class ActiveTurn
    {
        public required string RequestId { get; init; }
        public required CancellationTokenSource TimeoutCts { get; init; }
        public CancellationTokenRegistration TimeoutReg { get; set; }
        /// <summary>v2: ack messageId captured from the prompt response.</summary>
        public string? MessageId { get; set; }
    }

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
        /// <summary>Negotiated dialect — v1 until initialize completes.</summary>
        public IAcpDialect Dialect { get; set; } = AcpDialects.V1;
        /// <summary>Dialect-scoped turn lifecycle tracker.</summary>
        public ITurnTracker TurnTracker { get; set; } = new AcpV1TurnTracker();
        /// <summary>The in-flight prompt turn, if any (field — swapped via Interlocked).</summary>
        public ActiveTurn? Turn;
        public ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> PendingResponses { get; } = new();
        public ConcurrentDictionary<string, PendingPermission> PendingPermissions { get; } = new();
        /// <summary>Agent→client requests being handled (fs/*, terminal/*, elicitation) — cancelled via $/cancel_request.</summary>
        public ConcurrentDictionary<string, CancellationTokenSource> InFlightRequests { get; } = new();
        /// <summary>Tool calls without a terminal tool_call_update yet — closed as cancelled on session/cancel.</summary>
        public ConcurrentDictionary<string, byte> OpenToolCalls { get; } = new();
        /// <summary>v2 upserts: toolCallIds already seen — first update emits tool_call, later ones tool_output.</summary>
        public ConcurrentDictionary<string, byte> SeenToolCalls { get; } = new();
        /// <summary>Consent cache: (tool kind|title) → chosen optionId for *_always outcomes.</summary>
        public ConcurrentDictionary<string, string> AlwaysAnswers { get; } = new();
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

    /// <summary>Agent CLI bound to the thread's live session, if any.</summary>
    public AgentType? GetSessionAgentType(string threadId) =>
        _sessions.TryGetValue(threadId, out var holder) ? holder.Spawn.AgentType : null;

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

        // RF-001 + SPEC-20260921-acp-v2-readiness RF-202: ACP initialize with
        // the configured max protocol version (default 1 — v2 stays opt-in
        // while draft). The response carries the negotiated version, agent
        // capabilities and authMethods; the negotiated version selects the
        // per-connection dialect.
        var requested = Math.Clamp(_options.MaxProtocolVersion, 1, AcpDialects.MaxSupported);
        var offer = AcpDialects.For(requested)!;
        var initResult = await SendRequestAsync(holder, "initialize",
            offer.BuildInitializeParams(_options),
            _options.HandshakeTimeout, cancellationToken).ConfigureAwait(false);

        if (initResult is null)
        {
            EmitEvent(threadId, "error", "system",
                "Agent did not complete the ACP initialize handshake.", null);
            await StopSessionAsync(threadId, cancellationToken).ConfigureAwait(false);
            return false;
        }

        // The agent answers with the same version if supported, or its own
        // latest — so a v1-only agent transparently falls back to v1 even
        // when we offered 2. Anything above our offer or 0/undefined fails.
        var answered = initResult.Value.TryGetProperty("protocolVersion", out var pv)
            && pv.ValueKind == JsonValueKind.Number
            ? pv.GetInt32()
            : 0;
        var dialect = answered <= requested ? AcpDialects.For(answered) : null;
        if (dialect is null)
        {
            EmitEvent(threadId, "error", "system",
                $"Agent answered protocolVersion {answered}; this client speaks ACP up to v{requested}.",
                JsonSerializer.Serialize(new { code = "unsupported_version", requested, answered }));
            await StopSessionAsync(threadId, cancellationToken).ConfigureAwait(false);
            return false;
        }

        holder.Dialect = dialect;
        holder.TurnTracker = dialect.CreateTurnTracker();
        holder.Peer = dialect.ParseInitializeResult(initResult.Value);

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
            var newResult = await SendRequestAsync(holder, "session/new",
                holder.Dialect.BuildSessionNewParams(holder.Peer, workspacePath, BuildMcpServers(holder)),
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

    /// <summary>RF-012: configured MCP servers (e.g. RAG/Knowledge) handed to the agent.</summary>
    private object[] BuildMcpServers(SessionHolder holder)
    {
        var list = new List<object>();
        foreach (var server in _options.McpServers)
        {
            if (!string.IsNullOrWhiteSpace(server.Url))
            {
                if (!holder.Dialect.SupportsMcpTransport(holder.Peer, "http"))
                {
                    continue; // gated by the negotiated dialect's capabilities
                }

                var headers = server.Headers is { Count: > 0 }
                    ? server.Headers.Select(h => new { name = h.Key, value = h.Value }).ToArray()
                    : Array.Empty<object>();
                list.Add(new { type = "http", name = server.Name, url = server.Url, headers });
            }
            else if (!string.IsNullOrWhiteSpace(server.Command))
            {
                if (!holder.Dialect.SupportsMcpTransport(holder.Peer, "stdio"))
                {
                    continue;
                }

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

        var auth = await SendRequestAsync(holder, holder.Dialect.AuthenticateMethod,
            new { methodId = agentMethod.Id },
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

        // v1: session/resume (reattach) or session/load (replay); v2:
        // session/resume + replayFrom (session/load was removed).
        var reattach = holder.Dialect.BuildReattachRequest(
            holder.Peer, previousId, holder.Spawn.WorkspacePath, BuildMcpServers(holder));
        if (reattach is null)
        {
            return false;
        }

        // Replay flows stream history as session/update notifications before
        // answering — give them a longer timeout.
        var timeout = reattach.ExpectsReplay ? _options.RequestTimeout * 4 : _options.RequestTimeout;
        var result = await SendRequestAsync(holder, reattach.Method, reattach.Params, timeout, cancellationToken)
            .ConfigureAwait(false);

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

        // Real ACP: session/prompt takes content blocks. In v1 the response
        // only arrives when the turn ends (stopReason); in v2 it is an ack
        // ({messageId}) and the turn ends on an idle state_update. Either way
        // awaiting the response here would hold the HTTP endpoint — the id
        // stays registered and the dispatch loop resolves it.
        object promptParams = holder.SessionId is { } sessionId
            ? holder.Dialect.BuildPromptParams(sessionId, text)
            : (object)new { text, delivery };

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        holder.PendingResponses[id] = tcs;

        // RF-011: a stuck agent must not leak the pending turn forever.
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(holder.Cts.Token);
        timeoutCts.CancelAfter(_options.TurnTimeout);
        var turn = new ActiveTurn { RequestId = id, TimeoutCts = timeoutCts };
        holder.Turn = turn;
        turn.TimeoutReg = timeoutCts.Token.Register(() => OnTurnTimeout(holder, turn));

        _ = tcs.Task.ContinueWith(t =>
        {
            if (t is { IsCompletedSuccessfully: true, Status: TaskStatus.RanToCompletion })
            {
                if (holder.TurnTracker.PromptResponseEndsTurn)
                {
                    // v1: the prompt response carries stopReason and ends the turn.
                    var stopReason = t.Result.TryGetProperty("stopReason", out var sr)
                        ? sr.GetString() : null;
                    EndTurn(holder, turn, stopReason);
                }
                // v2: ack only — the turn stays open until an idle state_update.
            }
            else if (t.IsFaulted)
            {
                EmitEvent(threadId, "error", "system",
                    $"Prompt turn rejected: {t.Exception?.GetBaseException().Message}", null);
                ClearTurn(holder, turn);
            }
            else
            {
                ClearTurn(holder, turn);
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

    /// <summary>
    /// Closes the in-flight turn and emits the normalized "Prompt turn
    /// completed" session event — the same event v1 emitted, so downstream
    /// consumers (run client, timeline) see an identical stream on both
    /// dialects (SPEC-20260921-acp-v2-readiness AC-2).
    /// </summary>
    private void EndTurn(SessionHolder holder, ActiveTurn turn, string? stopReason)
    {
        if (!ClearTurn(holder, turn))
        {
            return;
        }

        EmitEvent(holder.ThreadId, "session", "system", "Prompt turn completed",
            JsonSerializer.Serialize(new { state = "ready", stopReason, messageId = turn.MessageId }));
    }

    /// <summary>Detaches the turn once — returns false when it was already closed.</summary>
    private static bool ClearTurn(SessionHolder holder, ActiveTurn turn)
    {
        if (Interlocked.CompareExchange(ref holder.Turn, null, turn) != turn)
        {
            return false;
        }

        try
        {
            turn.TimeoutReg.Dispose();
            turn.TimeoutCts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        return true;
    }

    /// <summary>
    /// RF-011: turn timeout. v1 faults the still-pending prompt RPC (the
    /// continuation emits the error). v2 already got its ack — cancel the
    /// turn on the wire and close it locally as cancelled.
    /// </summary>
    private void OnTurnTimeout(SessionHolder holder, ActiveTurn turn)
    {
        if (holder.PendingResponses.TryRemove(turn.RequestId, out var pending))
        {
            pending.TrySetException(new AcpException(AcpErrorCode.TurnTimeout, "session/prompt",
                $"turn exceeded {_options.TurnTimeout}"));
            return;
        }

        if (!ClearTurn(holder, turn))
        {
            return;
        }

        EmitEvent(holder.ThreadId, "error", "system",
            $"Prompt turn exceeded {_options.TurnTimeout}.",
            JsonSerializer.Serialize(new { code = "turn_timeout" }));
        EmitEvent(holder.ThreadId, "session", "system", "Prompt turn completed",
            JsonSerializer.Serialize(new { state = "ready", stopReason = "cancelled", turn.MessageId }));
        _ = CancelAsync(holder.ThreadId);
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

        // Auto-detect boolean options from the negotiated configOptions —
        // callers only carry the value as text.
        if (!isBoolean)
        {
            isBoolean = IsBooleanConfigOption(holder.Peer.ConfigOptions, configId);
        }

        var p = holder.Dialect.BuildSetConfigOptionParams(holder.SessionId, configId, value, isBoolean);

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

    /// <summary>True when the advertised config option is a boolean toggle.</summary>
    private static bool IsBooleanConfigOption(JsonElement? configOptions, string configId)
    {
        if (configOptions is not { } opts || opts.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        try
        {
            foreach (var opt in opts.EnumerateArray())
            {
                // v1 uses "id"; v2-readiness accepts "configId" too.
                var id = opt.TryGetProperty("id", out var i) ? i.GetString()
                    : opt.TryGetProperty("configId", out var ci) ? ci.GetString()
                    : null;
                if (id == configId)
                {
                    return opt.TryGetProperty("type", out var t)
                        && string.Equals(t.GetString(), "boolean", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    /// <summary>RF-002: diagnostic — sessions the agent still knows about (capability-gated).</summary>
    public async Task<IReadOnlyList<JsonElement>> ListSessionsAsync(
        string threadId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || !IsAlive(holder) || !holder.Peer.SessionList)
        {
            return [];
        }

        var result = await SendRequestAsync(holder, "session/list", new { },
            _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return [];
        }

        return result.Value.TryGetProperty("sessions", out var s) && s.ValueKind == JsonValueKind.Array
            ? s.EnumerateArray().Select(e => e.Clone()).ToArray()
            : [];
    }

    /// <summary>RF-002: delete an agent-side session (capability-gated).</summary>
    public async Task<bool> DeleteSessionAsync(
        string threadId, string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || !IsAlive(holder) || !holder.Peer.SessionDelete)
        {
            return false;
        }

        var result = await SendRequestAsync(holder, "session/delete", new { sessionId },
            _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
        if (result is not null && holder.SessionId == sessionId)
        {
            _lastSessionIds.TryRemove(threadId, out _);
        }

        return result is not null;
    }

    /// <summary>RF-004: agent logout via the control plane (capability-gated).</summary>
    public async Task<bool> LogoutAsync(string threadId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || !IsAlive(holder) || !holder.Peer.AuthLogout)
        {
            return false;
        }

        var result = await SendRequestAsync(holder, holder.Dialect.LogoutMethod, new { },
            _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    /// <summary>RF-003: legacy mode switching for agents that only expose modes (v1-only — v2 removed set_mode).</summary>
    public async Task<bool> SetModeAsync(string threadId, string modeId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(threadId, out var holder) || !IsAlive(holder) || holder.SessionId is null
            || holder.Dialect.SetModeMethod is not { } setModeMethod)
        {
            return false;
        }

        var result = await SendRequestAsync(holder, setModeMethod,
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
            var payload = new
            {
                jsonrpc = "2.0",
                method = "session/cancel",
                @params = holder.Dialect.BuildCancelParams(holder.SessionId)
            };

            await WriteLineAsync(holder, payload, cancellationToken).ConfigureAwait(false);

            // The spec requires answering every pending permission request with
            // the cancelled outcome so the agent can unwind its turn.
            foreach (var (requestId, pending) in holder.PendingPermissions.ToArray())
            {
                if (holder.PendingPermissions.TryRemove(requestId, out var removed))
                {
                    removed.TimeoutCts.Cancel();
                    removed.TimeoutCts.Dispose();
                    await WriteLineAsync(holder, new
                    {
                        jsonrpc = "2.0",
                        id = removed.JsonRpcId,
                        result = new { outcome = new { outcome = "cancelled" } }
                    }, cancellationToken).ConfigureAwait(false);
                }
            }

            // RF-005: tool calls that never received a terminal update are
            // marked cancelled locally so the timeline doesn't show them stuck.
            foreach (var toolCallId in holder.OpenToolCalls.Keys)
            {
                if (holder.OpenToolCalls.TryRemove(toolCallId, out _))
                {
                    EmitEvent(threadId, AgentEventKinds.ToolOutput, "assistant", null,
                        JsonSerializer.Serialize(new { toolCallId, status = "cancelled" }),
                        toolCallId: toolCallId);
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
            pending.TimeoutCts.Cancel();
            pending.TimeoutCts.Dispose();

            var optionId = MapOutcomeToOption(outcome, pending.Options);

            // RF-006: a *_always choice is remembered per tool kind so future
            // requests are auto-answered (audited with auto:true on emit).
            var chosen = optionId is null
                ? null
                : pending.Options.FirstOrDefault(o => o.OptionId == optionId);
            if (optionId is not null
                && chosen?.Kind?.EndsWith("_always", StringComparison.OrdinalIgnoreCase) == true)
            {
                var toolKey = ExtractToolKey(null, pending.RawLine);
                if (toolKey is not null)
                {
                    holder.AlwaysAnswers[toolKey] = optionId;
                }
            }

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

            // RF-002: graceful session/close awaited briefly when the agent
            // supports it — must run BEFORE Cts.Cancel() so the response can
            // arrive; kill is the fallback after the grace period.
            if (holder.SessionId is not null && holder.Peer.SessionClose && IsAlive(holder))
            {
                try
                {
                    await SendRequestAsync(holder, "session/close",
                        new { sessionId = holder.SessionId },
                        _options.CloseTimeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Channel already broken — fall through to kill.
                }
            }

            holder.Cts.Cancel();

            foreach (var pending in holder.PendingResponses.Values)
            {
                pending.TrySetCanceled();
            }

            CleanupPendingState(holder);
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

        CleanupPendingState(holder);
        EmitEvent(holder.ThreadId, "lifecycle", "system", "Agent process exited",
            JsonSerializer.Serialize(new { state = "dead", exitCode, sessionId = holder.SessionId }));
    }

    /// <summary>Releases permission timeout registrations and in-flight tool request tokens.</summary>
    private static void CleanupPendingState(SessionHolder holder)
    {
        foreach (var (key, _) in holder.PendingPermissions)
        {
            if (holder.PendingPermissions.TryRemove(key, out var pending))
            {
                try
                {
                    pending.TimeoutCts.Cancel();
                    pending.TimeoutCts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // $/cancel_request already disposed it.
                }
            }
        }

        foreach (var (key, _) in holder.InFlightRequests)
        {
            if (holder.InFlightRequests.TryRemove(key, out var cts))
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // $/cancel_request already disposed it.
                }
            }
        }

        holder.OpenToolCalls.Clear();
        holder.SeenToolCalls.Clear();
        holder.Turn = null;
    }

    private void HandleStdout(string threadId, string line)
    {
        if (!_sessions.TryGetValue(threadId, out var holder))
        {
            return;
        }

        try
        {
            // RF-208: NDJSON batch — a single line may carry a JSON array of
            // JSON-RPC messages; each entry is dispatched independently and
            // invalid entries get a per-entry -32600 response.
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in doc.RootElement.EnumerateArray())
                {
                    DispatchEntry(holder, entry);
                }

                return;
            }

            var parsed = AcpProtocolParser.ParseElement(doc.RootElement, holder.Dialect);
            if (parsed is null)
            {
                EmitEvent(threadId, "message", "assistant", line, null);
                return;
            }

            DispatchParsed(holder, parsed, line);
        }
        catch
        {
            EmitEvent(threadId, "message", "assistant", line, null);
        }
    }

    /// <summary>Dispatches one entry of a batch array (RF-208).</summary>
    private void DispatchEntry(SessionHolder holder, JsonElement entry)
    {
        // A batch entry must be a JSON-RPC object — anything else (or an
        // object with neither method nor id) gets a per-entry -32600.
        var valid = entry.ValueKind == JsonValueKind.Object
            && (entry.TryGetProperty("method", out _) || entry.TryGetProperty("id", out _));
        if (!valid)
        {
            _ = WriteLineAsync(holder, new
            {
                jsonrpc = "2.0",
                id = (string?)null,
                error = new { code = -32600, message = "invalid request" }
            }, CancellationToken.None);
            return;
        }

        var parsed = AcpProtocolParser.ParseElement(entry, holder.Dialect);
        if (parsed is not null)
        {
            DispatchParsed(holder, parsed, entry.GetRawText());
        }
    }

    private void DispatchParsed(SessionHolder holder, AcpProtocolParser.Parsed parsed, string rawLine)
    {
        var threadId = holder.ThreadId;
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
                        // Capture the ack (v2 messageId) on the read loop so a
                        // fast-following state_update already sees it.
                        if (holder.Turn is { } openTurn && openTurn.RequestId == rid)
                        {
                            holder.TurnTracker.OnPromptResponse(parsed.ResponseResult);
                            if (holder.TurnTracker is AcpV2TurnTracker v2)
                            {
                                openTurn.MessageId = v2.MessageId;
                            }
                        }

                        tcs.TrySetResult(parsed.ResponseResult);
                    }
                }
                return;

            case AcpProtocolParser.MessageType.Request:
                HandleAgentRequest(holder, parsed, rawLine);
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

                // v2: an idle state_update closes the open turn (v1 closes on
                // the prompt response — see the SendPromptAsync continuation).
                if (holder.Turn is { } turn
                    && holder.TurnTracker.TryCompleteTurn(parsed.Params, out var turnStopReason))
                {
                    EndTurn(holder, turn, turnStopReason);
                }

                var kind = parsed.Kind;
                var patchOp = parsed.PatchOp;

                // v2 tool_call_update is upsert-only: the first update for a
                // toolCallId creates the card (tool_call), later ones patch it
                // (tool_output). v1 never sets IsToolCallUpsert.
                if (parsed.IsToolCallUpsert && parsed.ToolCallId is { } upId)
                {
                    if (holder.SeenToolCalls.TryAdd(upId, 1))
                    {
                        kind = AgentEventKinds.ToolCall;
                        patchOp = AgentPatchOps.Append;
                    }
                    else
                    {
                        kind = AgentEventKinds.ToolOutput;
                    }
                }

                // Track open tool calls so session/cancel can close them.
                if (parsed.ToolCallId is { } tcId)
                {
                    if (kind == AgentEventKinds.ToolCall && !IsTerminalToolUpdate(parsed.PayloadJson))
                    {
                        holder.OpenToolCalls[tcId] = 1;
                    }
                    else if (kind == AgentEventKinds.ToolOutput && IsTerminalToolUpdate(parsed.PayloadJson))
                    {
                        holder.OpenToolCalls.TryRemove(tcId, out _);
                    }
                }

                EmitEvent(threadId, kind, parsed.Role ?? "assistant", parsed.Content, parsed.PayloadJson,
                    parsed.SessionId, parsed.ToolCallId,
                    parsed.MessageId, parsed.PlanId, patchOp);
                return;
        }
    }

    private static bool IsTerminalToolUpdate(string? payloadJson)
    {
        if (payloadJson is null)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("status", out var s)
                && s.GetString() is "completed" or "failed" or "cancelled";
        }
        catch (JsonException)
        {
            return false;
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

            // RF-006: allow_always/reject_always consent cache — identical tool
            // kinds are auto-answered and audited with auto:true.
            var toolKey = ExtractToolKey(parsed.PayloadJson, rawLine);
            if (toolKey is not null
                && holder.AlwaysAnswers.TryGetValue(toolKey, out var cachedOptionId))
            {
                EmitEvent(holder.ThreadId, "permission", "assistant", parsed.Content,
                    InjectAutoFlag(parsed.PayloadJson));
                _ = WriteLineAsync(holder, new
                {
                    jsonrpc = "2.0",
                    id = rpcId,
                    result = new { outcome = new { outcome = "selected", optionId = cachedOptionId } }
                }, CancellationToken.None);
                return;
            }

            var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(holder.Cts.Token);
            holder.PendingPermissions[effectiveId] = new PendingPermission(rpcId, options, timeoutCts, rawLine);
            EmitEvent(holder.ThreadId, "permission", "assistant", parsed.Content, parsed.PayloadJson);

            // RF-006: unanswered permissions auto-cancel after PermissionTimeout.
            timeoutCts.CancelAfter(_options.PermissionTimeout);
            _ = timeoutCts.Token.Register(() =>
            {
                if (holder.PendingPermissions.TryRemove(effectiveId, out var expired))
                {
                    _ = WriteLineAsync(holder, new
                    {
                        jsonrpc = "2.0",
                        id = expired.JsonRpcId,
                        result = new { outcome = new { outcome = "cancelled" } }
                    }, CancellationToken.None);
                }
            });
            return;
        }

        // SPEC-20260921-acp-v2-readiness AC-4: methods the negotiated dialect
        // removed (v2: fs/*, terminal/*) must never be dispatched — answer
        // -32601 so the agent does not hang on a deleted surface.
        if (!holder.Dialect.AllowsClientMethod(parsed.Method))
        {
            _ = WriteLineAsync(holder, new
            {
                jsonrpc = "2.0",
                id = parsed.RequestId,
                error = new { code = -32601, message = $"Method '{parsed.Method}' was removed in ACP v{holder.Dialect.ProtocolVersion}." }
            }, CancellationToken.None);
            return;
        }

        // RF-008/009: fs/*, terminal/*, elicitation/* and extension methods go to
        // the client tool handler when one is registered; otherwise -32601 so
        // the agent does not hang on an undeclared capability.
        var requestCts = parsed.RequestId is { } reqId
            ? holder.InFlightRequests.GetOrAdd(reqId,
                _ => CancellationTokenSource.CreateLinkedTokenSource(holder.Cts.Token))
            : null;

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
                            holder.Spawn.WorkspacePath,
                            parsed.Method, parsed.Params, requestCts?.Token ?? holder.Cts.Token).ConfigureAwait(false);
                        response = new { jsonrpc = "2.0", id = parsed.RequestId, result };
                    }
                    catch (AcpException aex)
                    {
                        response = new { jsonrpc = "2.0", id = parsed.RequestId, error = new { code = ToJsonRpcCode(aex.Code), message = aex.Message } };
                    }
                    catch (OperationCanceledException)
                    {
                        response = new { jsonrpc = "2.0", id = parsed.RequestId, error = new { code = -32800, message = "request cancelled" } };
                    }
                    catch (Exception ex)
                    {
                        // IO/process failures still owe the agent a response —
                        // otherwise it waits on a request that never resolves.
                        response = new { jsonrpc = "2.0", id = parsed.RequestId, error = new { code = -32603, message = ex.Message } };
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

                // If the agent cancelled meanwhile, the cancel path already
                // answered -32800 — skip the double response.
                if (parsed.RequestId is null || holder.InFlightRequests.TryRemove(parsed.RequestId, out _))
                {
                    await WriteLineAsync(holder, response, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
                // Channel failed — the session will die via the read loop.
            }
            finally
            {
                requestCts?.Dispose();
            }
        });
        EmitEvent(holder.ThreadId, "activity", "system",
            $"Agent request '{parsed.Method}'.", rawLine);
    }

    /// <summary>
    /// RF-005: <c>$/cancel_request</c> — the agent cancelled a request it sent
    /// us (permission, fs/*, terminal/*). The request is answered with JSON-RPC
    /// -32800, in-flight tool work is cancelled and pending permissions keyed by
    /// that id are dropped so they can no longer be answered.
    /// </summary>
    private void HandleAgentCancelRequest(SessionHolder holder, AcpProtocolParser.Parsed parsed)
    {
        if (parsed.Params.ValueKind != JsonValueKind.Object
            || !parsed.Params.TryGetProperty("id", out var idEl))
        {
            return;
        }

        var id = idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : idEl.GetRawText();
        if (id is null)
        {
            return;
        }

        if (holder.InFlightRequests.TryRemove(id, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _ = WriteLineAsync(holder, new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = -32800, message = "request cancelled" }
            }, CancellationToken.None);
        }

        foreach (var (key, pending) in holder.PendingPermissions)
        {
            if (pending.JsonRpcId == id
                && holder.PendingPermissions.TryRemove(key, out var dropped))
            {
                dropped.TimeoutCts.Cancel();
                dropped.TimeoutCts.Dispose();
            }
        }
    }

    /// <summary>Consent-cache key: toolCall.kind preferred, else title (RF-006).</summary>
    private static string? ExtractToolKey(string? payloadJson, string rawLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawLine);
            if (doc.RootElement.TryGetProperty("params", out var p)
                && p.TryGetProperty("toolCall", out var tc) && tc.ValueKind == JsonValueKind.Object)
            {
                if (tc.TryGetProperty("kind", out var k) && k.GetString() is { } kind)
                {
                    return $"kind:{kind}";
                }

                if (tc.TryGetProperty("title", out var t) && t.GetString() is { } title)
                {
                    return $"title:{title}";
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string InjectAutoFlag(string? payloadJson)
    {
        if (payloadJson is null)
        {
            return """{"auto":true}""";
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);
            if (dict is not null)
            {
                dict["auto"] = JsonSerializer.SerializeToElement(true);
                return JsonSerializer.Serialize(dict);
            }
        }
        catch (JsonException)
        {
        }

        return payloadJson;
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

        WithoutHarnessEnv.RemoveFrom(startInfo.Environment);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            // Missing binary/bad workdir must degrade like the TCP path —
            // an error event + null, never an exception through the endpoint.
            EmitEvent(threadId, "error", "system", $"Could not start agent process: {ex.Message}", null);
            return null;
        }

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
                    mcpStdio = holder.Peer.McpStdio,
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
        string? sessionId = null, string? toolCallId = null,
        string? messageId = null, string? planId = null, string? patchOp = null)
    {
        var evt = new AgentSessionEvent(threadId, DateTimeOffset.UtcNow, kind, role, content, payload,
            SessionId: sessionId, ToolCallId: toolCallId,
            MessageId: messageId, PlanId: planId, PatchOp: patchOp);
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
