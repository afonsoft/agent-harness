namespace Taskboard.Agents;

/// <summary>
/// Tier de custo/qualidade do modelo escolhido para uma execução de agente
/// (SPEC-20260918-agent-model-tiers RF-001). Ausente em payloads antigos → Normal.
/// </summary>
public enum AgentModelTier
{
    Lite,
    Normal,
    Ultra,
}
