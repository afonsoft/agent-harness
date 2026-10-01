using System.Diagnostics;
using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Harness;
using Taskboard.ValueObjects;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// One-shot agent run over a real ACP session (SPEC-20260921-acp-v1-conformance
/// RF-010): initialize → session/new → session/prompt → consume session/update
/// notifications until the turn ends → session/close. Gives board/cockpit runs
/// the same structured events as interactive threads. Falls back to the
/// args-mode client when the agent does not speak ACP.
/// </summary>
public sealed class AcpSessionRunClient : IAgentAcpClient
{
    private readonly AcpSessionClient _sessions;
    private readonly IAgentAcpClient _fallback;

    public AcpSessionRunClient(AcpSessionClient sessions, IAgentAcpClient fallback)
    {
        _sessions = sessions;
        _fallback = fallback;
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        IProgress<AgentLogMessage> progress,
        CancellationToken cancellationToken = default)
    {
        var key = $"run:{request.IssueId}:{Guid.NewGuid():N}";
        var stopwatch = Stopwatch.StartNew();
        var listener = new TurnListener(_sessions, key, request, progress);

        _sessions.RegisterEventListener(key, listener.OnEvent);
        try
        {
            var started = await _sessions.StartSessionAsync(
                key,
                request.AgentType,
                request.RepoPath ?? Environment.CurrentDirectory,
                Sandbox.WorkspaceWrite,
                request.ResolvedModelName,
                cancellationToken,
                request.ContainerContext).ConfigureAwait(false);

            if (!started)
            {
                // Not an ACP agent — keep the args-mode path.
                _sessions.UnregisterEventListener(key);
                return await _fallback.ExecuteAsync(request, progress, cancellationToken).ConfigureAwait(false);
            }

            var sent = await _sessions.SendPromptAsync(key, request.Instructions, "queue", cancellationToken)
                .ConfigureAwait(false);
            if (!sent)
            {
                await _sessions.StopSessionAsync(key, cancellationToken).ConfigureAwait(false);
                return await _fallback.ExecuteAsync(request, progress, cancellationToken).ConfigureAwait(false);
            }

            using var cancelReg = cancellationToken.Register(() =>
            {
                _ = _sessions.CancelAsync(key, CancellationToken.None);
                // The run is over from our side whether or not the agent still
                // answers the pending prompt — complete so the caller unwinds.
                listener.CompleteCancelled();
            });

            var stopReason = await listener.TurnDone.ConfigureAwait(false);
            await _sessions.StopSessionAsync(key, cancellationToken).ConfigureAwait(false);

            return new AgentExecutionResult(
                stopReason is "cancelled" ? 130 : 0,
                stopReason is not "cancelled",
                Usage: listener.LatestUsage,
                Duration: stopwatch.Elapsed,
                ModelUsed: request.ResolvedModelName);
        }
        catch (OperationCanceledException)
        {
            await _sessions.StopSessionAsync(key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (AcpException)
        {
            await _sessions.StopSessionAsync(key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _sessions.UnregisterEventListener(key);
        }
    }

    // Tracks a single prompt turn: forwards events to the progress log and
    // completes TurnDone when the turn ends, the process dies, or the run is
    // cancelled.
    private sealed class TurnListener(
        AcpSessionClient sessions,
        string key,
        AgentExecutionRequest request,
        IProgress<AgentLogMessage>? progress)
    {
        private readonly TaskCompletionSource<string?> _turnDone =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TokenUsage? LatestUsage { get; private set; }

        public Task<string?> TurnDone => _turnDone.Task;

        public void CompleteCancelled() => _turnDone.TrySetResult("cancelled");

        public void OnEvent(AgentSessionEvent e)
        {
            progress?.Report(new AgentLogMessage(
                e.Timestamp,
                request.IssueId,
                e.Kind == "error" ? AgentLogStream.StdErr : AgentLogStream.System,
                e.Content ?? e.PayloadJson ?? string.Empty,
                e.Kind,
                e.PayloadJson));

            switch (e.Kind)
            {
                case "permission":
                    HandlePermission(e);
                    break;
                case AgentEventKinds.Metric:
                    HandleMetric(e);
                    break;
                case "session" when e.Content == "Prompt turn completed":
                    _turnDone.TrySetResult(ExtractStopReason(e.PayloadJson));
                    break;
                case "lifecycle" when e.PayloadJson?.Contains("\"dead\"") == true:
                    _turnDone.TrySetException(new AcpException(AcpErrorCode.ProcessDied, "run", "agent process exited"));
                    break;
                case "error":
                    HandleError(e);
                    break;
            }
        }

        private void HandlePermission(AgentSessionEvent ev)
        {
            // Headless run: auto-allow — the args-mode path already runs
            // every CLI with bypass flags; here we keep the audit trail.
            var requestId = ExtractRequestId(ev.PayloadJson);
            if (requestId is not null)
            {
                _ = sessions.ReplyPermissionAsync(key, requestId, "allow", CancellationToken.None);
            }
        }

        private void HandleMetric(AgentSessionEvent ev)
        {
            // ACP usage_update carries { used, size, cost? } — context
            // window consumption. Best-effort map onto TokenUsage so
            // AgentExecutionResult.Usage is populated like args-mode.
            if (ExtractUsage(ev.PayloadJson) is { } usage)
            {
                LatestUsage = usage;
            }
        }

        private void HandleError(AgentSessionEvent ev)
        {
            if (ev.Content?.Contains("handshake", StringComparison.OrdinalIgnoreCase) == true
                || ev.Content?.Contains("session/new", StringComparison.OrdinalIgnoreCase) == true)
            {
                _turnDone.TrySetException(new AcpException(AcpErrorCode.Internal, "run", ev.Content));
            }
        }
    }

    private static string? ExtractRequestId(string? payloadJson)
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

    private static TokenUsage? ExtractUsage(string? payloadJson)
    {
        if (payloadJson is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var update = doc.RootElement.TryGetProperty("update", out var u) ? u : doc.RootElement;
            if (!update.TryGetProperty("used", out var used) || !used.TryGetInt64(out var tokens))
            {
                return null;
            }

            return new TokenUsage(tokens, 0, 0, 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractStopReason(string? payloadJson)
    {
        if (payloadJson is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("stopReason", out var s) ? s.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
