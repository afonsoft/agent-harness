using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Terminal;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261020-session-history-parity RF-003: live sessions inventory over
/// the two runtime sources — interactive PTY terminals and ACP sessions.
/// </summary>
public sealed class LiveAgentSessionCatalog(
    TerminalSessionManager terminals,
    AcpSessionClient acp) : ILiveAgentSessionCatalog
{
    public IReadOnlyList<LiveAgentSessionDto> Snapshot() =>
        [.. terminals.LiveSnapshot(), .. acp.LiveSnapshot()];
}
