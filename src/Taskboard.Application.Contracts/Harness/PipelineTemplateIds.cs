namespace Taskboard.Dtos;

/// <summary>Well-known pipeline template ids shared by server endpoints and clients.</summary>
public static class PipelineTemplateIds
{
    /// <summary>
    /// One parametrizable `AgentWork` stage plus an optional `Verification`
    /// stage — the only template accepting agent/tier/verification overrides
    /// (SPEC-20260920-board-cockpit-unified-runs R1).
    /// </summary>
    public const string SingleAgent = "single-agent";
}
