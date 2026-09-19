namespace Taskboard.Agents;

/// <summary>
/// Informações de um agente CLI detectado no servidor.
/// </summary>
public sealed record AgentInfo(
    string Name,
    string ExecutablePath,
    AgentType Type,
    AgentStatus Status,
    string? Version,
    string? Description,
    bool SupportsInteractiveSession = false);
