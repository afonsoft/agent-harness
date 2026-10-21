using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Terminal;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261020-agent-hibernation RF-002/RF-007: session-key → PTY adapter.
/// <c>term:{id}</c> keys map to <see cref="TerminalSessionManager"/> sessions;
/// respawn injects the resume env markers and reuses the deterministic id.
/// </summary>
public sealed class AgentPtyLifecycle(TerminalSessionManager terminals)
    : ISessionTerminator, ISessionRespawner
{
    public bool IsAlive(string sessionKey) =>
        TerminalId(sessionKey) is { } id && terminals.IsAlive(id);

    public Task<bool> TerminateAsync(string sessionKey, CancellationToken ct = default) =>
        TerminalId(sessionKey) is { } id
            ? terminals.KillAsync(id)
            : Task.FromResult(false);

    public Task<bool> RespawnAsync(
        string sessionKey, string userKey, IReadOnlyList<string> argv,
        string? cwd, CancellationToken ct = default) =>
        TerminalId(sessionKey) is { } id && argv.Count > 0
            ? terminals.RespawnDetachedAsync(userKey, id, cwd, argv,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [HibernationEnv.Resumed] = "1",
                    [HibernationEnv.ResumeOf] = sessionKey,
                })
            : Task.FromResult(false);

    private static string? TerminalId(string sessionKey) =>
        sessionKey.StartsWith("term:", StringComparison.Ordinal)
            ? sessionKey["term:".Length..]
            : null;
}
