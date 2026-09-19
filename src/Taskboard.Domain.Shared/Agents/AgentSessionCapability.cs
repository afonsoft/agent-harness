namespace Taskboard.Agents;

/// <summary>
/// Descreve as capacidades de sessão interativa suportadas por um CLI de agente.
/// </summary>
public sealed record AgentSessionCapability(
    bool SupportsInteractiveSession,
    bool SupportsSteer,
    bool SupportsInlinePermissions);
