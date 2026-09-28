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
    bool SupportsInteractiveSession = false,
    /// <summary>SPEC-20260928-ai-code-generic-cli RF-001: native transport — "acp" (structured) or "pty" (terminal).</summary>
    string Transport = "acp");
