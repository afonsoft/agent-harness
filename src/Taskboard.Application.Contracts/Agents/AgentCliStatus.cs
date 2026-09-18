using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>Detected auth state of an agent CLI (credential-file existence probe).</summary>
public enum AgentCliAuthStatus
{
    Unknown,
    Authenticated,
    NotAuthenticated
}

/// <summary>
/// Snapshot of an agent CLI on this machine (SPEC-20260917-cli-agents-terminal):
/// PATH detection, version probe and credential-file existence.
/// </summary>
public sealed record AgentCliStatus(
    AgentCliKind Agent,
    string DisplayName,
    string Binary,
    bool Installed,
    string? Version,
    AgentCliAuthStatus AuthStatus,
    string ConfigDir,
    string LoginCommand,
    string InstallHint,
    string RequiredTool,
    bool PrerequisiteMet);
