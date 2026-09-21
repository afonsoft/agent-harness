namespace Taskboard.Agents;

/// <summary>
/// Comando local a ser executado por um adaptador de agente.
/// <paramref name="TcpPort"/> — when set, the ACP channel is a TCP connection
/// to an already-running agent server (e.g. <c>copilot --acp --port</c>)
/// instead of a spawned subprocess (SPEC-20260921-acp-v1-conformance RF-014).
/// </summary>
public sealed record AgentCommand(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    int? TcpPort = null);
