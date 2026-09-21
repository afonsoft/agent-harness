using System.Diagnostics;
using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.AiChat;
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
        var turnDone = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnEvent(AgentSessionEvent e)
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
                    // Headless run: auto-allow — the args-mode path already runs
                    // every CLI with bypass flags; here we keep the audit trail.
                    var requestId = ExtractRequestId(e.PayloadJson);
                    if (requestId is not null)
                    {
                        _ = _sessions.ReplyPermissionAsync(key, requestId, "allow");
                    }
                    break;
                case "session" when e.Content == "Prompt turn completed":
                    turnDone.TrySetResult(ExtractStopReason(e.PayloadJson));
                    break;
                case "lifecycle" when e.PayloadJson?.Contains("\"dead\"") == true:
                    turnDone.TrySetException(new AcpException(AcpErrorCode.ProcessDied, "run", "agent process exited"));
                    break;
                case "error":
                    if (e.Content?.Contains("handshake", StringComparison.OrdinalIgnoreCase) == true
                        || e.Content?.Contains("session/new", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        turnDone.TrySetException(new AcpException(AcpErrorCode.Internal, "run", e.Content));
                    }
                    break;
            }
        }

        _sessions.RegisterEventListener(key, OnEvent);
        try
        {
            var started = await _sessions.StartSessionAsync(
                key,
                request.AgentType,
                request.RepoPath ?? Environment.CurrentDirectory,
                Sandbox.WorkspaceWrite,
                request.ResolvedModelName,
                cancellationToken).ConfigureAwait(false);

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
                _ = _sessions.CancelAsync(key);
            });

            var stopReason = await turnDone.Task.ConfigureAwait(false);
            await _sessions.StopSessionAsync(key, cancellationToken).ConfigureAwait(false);

            return new AgentExecutionResult(
                stopReason is "cancelled" ? 130 : 0,
                stopReason is not "cancelled",
                Duration: stopwatch.Elapsed,
                ModelUsed: request.ResolvedModelName);
        }
        catch (OperationCanceledException)
        {
            await _sessions.StopSessionAsync(key).ConfigureAwait(false);
            throw;
        }
        catch (AcpException)
        {
            await _sessions.StopSessionAsync(key).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _sessions.UnregisterEventListener(key);
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
