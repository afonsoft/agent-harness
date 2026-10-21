using Taskboard;
using Taskboard.Agents;
using Taskboard.Application.AiChat;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Agents;
using Taskboard.Requests;
using Taskboard.ValueObjects;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261020-session-history-parity RF-007 (P3): "Resume in native chat"
/// — creates an agent-mode thread bound to the CLI session's cwd, primes the
/// ACP client with the native session id (session/resume or session/load
/// replay) and starts the session so history streams into the thread.
/// </summary>
public sealed class SessionNativeChatService(
    AiChatService aiChat,
    AcpSessionClient acp,
    AgentSessionManager sessions,
    ILogger<SessionNativeChatService> logger)
{
    /// <summary>CLIs with a native-chat adapter — same gate as the index.</summary>
    private static readonly HashSet<string> Supported =
        new(StringComparer.Ordinal) { "claude", "codex", "opencode" };

    public async Task<NativeChatResumeResult> StartAsync(
        NativeChatResumeRequest request, CancellationToken ct = default)
    {
        if (!Supported.Contains(request.Cli)
            || !Enum.TryParse<AgentType>(request.Cli, ignoreCase: true, out var agentType))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"CLI '{request.Cli}' has no native-chat adapter.");
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "sessionId is required.");
        }

        var thread = await aiChat.CreateThreadAsync(
            new CreateAiChatThreadRequest(
                Title: $"resume {request.Cli} {request.SessionId[..Math.Min(8, request.SessionId.Length)]}",
                Model: "default",
                ReasoningEffort: "medium",
                Sandbox: "workspace-write",
                Mode: "agent",
                AgentType: agentType.ToString(),
                WorkspacePath: request.Cwd),
            Actor.LocalUser(), ct).ConfigureAwait(false);

        var threadId = thread.Id;
        acp.PrimeResumeSessionId(threadId, request.SessionId);

        var started = await sessions.EnsureSessionAsync(threadId, ct).ConfigureAwait(false);
        logger.LogInformation(
            "native-chat resume: cli={Cli} session={SessionId} thread={ThreadId} started={Started}",
            request.Cli, request.SessionId, threadId, started);

        return new NativeChatResumeResult(threadId, $"issue:{threadId}");
    }
}
