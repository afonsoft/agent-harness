using Taskboard.Agents;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Dtos;
using Taskboard.Harness;

namespace Taskboard.Application.Harness;

/// <summary>
/// Pre-configured pipeline templates
/// (SPEC-20260919-ade-multi-agent-orchestration RF-001).
/// </summary>
public static class PipelineTemplates
{
    private const string RoleBuilder = "builder";
    private const string RoleBuilderDisplay = "Builder";
    private const string RoleVerifier = "verifier";
    private const string RoleVerifierDisplay = "Verifier";
    private const string RoleArchitect = "architect";

    public static readonly PipelineDefinition StandardFeature = new(
        "standard-feature", "Standard Feature",
        [
            new PipelineStage(RoleArchitect, "Architect", PipelineStageKind.AgentWork,
                AgentRole.Architect, AgentType.Claude, AgentModelTier.Ultra, []),
            new PipelineStage("approve-plan", "Approval", PipelineStageKind.Approval,
                null, null, AgentModelTier.Normal, [RoleArchitect]),
            new PipelineStage(RoleBuilder, RoleBuilderDisplay, PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.OpenCode, AgentModelTier.Normal, ["approve-plan"]),
            new PipelineStage(RoleVerifier, RoleVerifierDisplay, PipelineStageKind.Verification,
                null, null, AgentModelTier.Normal, [RoleBuilder]),
            new PipelineStage("reviewer", "Reviewer", PipelineStageKind.AgentWork,
                AgentRole.Reviewer, AgentType.Devin, AgentModelTier.Normal, [RoleVerifier]),
        ]);

    public static readonly PipelineDefinition QuickPatch = new(
        "quick-patch", "Quick Patch",
        [
            new PipelineStage(RoleBuilder, RoleBuilderDisplay, PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, []),
            new PipelineStage(RoleVerifier, RoleVerifierDisplay, PipelineStageKind.Verification,
                null, null, AgentModelTier.Normal, [RoleBuilder]),
        ]);

    /// <summary>
    /// Ad-hoc single-agent run — the only template that accepts
    /// `AgentOverride`/`TierOverride`/`SkipVerification`
    /// (SPEC-20260920-board-cockpit-unified-runs R1/R2).
    /// </summary>
    public const string SingleAgentId = PipelineTemplateIds.SingleAgent;

    public static readonly PipelineDefinition SingleAgent = new(
        SingleAgentId, "Single Agent",
        [
            new PipelineStage(RoleBuilder, RoleBuilderDisplay, PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, []),
            new PipelineStage(RoleVerifier, RoleVerifierDisplay, PipelineStageKind.Verification,
                null, null, AgentModelTier.Normal, [RoleBuilder]),
        ]);

    public static readonly PipelineDefinition TestDriven = new(
        "test-driven", "Test Driven",
        [
            new PipelineStage(RoleArchitect, "Architect", PipelineStageKind.AgentWork,
                AgentRole.Architect, AgentType.Claude, AgentModelTier.Normal, []),
            new PipelineStage("tester", "Tester", PipelineStageKind.AgentWork,
                AgentRole.Tester, AgentType.Codex, AgentModelTier.Normal, [RoleArchitect]),
            new PipelineStage(RoleBuilder, RoleBuilderDisplay, PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.OpenCode, AgentModelTier.Normal, ["tester"]),
            new PipelineStage(RoleVerifier, RoleVerifierDisplay, PipelineStageKind.Verification,
                null, null, AgentModelTier.Normal, [RoleBuilder]),
        ]);

    public static IReadOnlyList<PipelineDefinition> All { get; } =
        [StandardFeature, QuickPatch, TestDriven, SingleAgent];

    public static PipelineDefinition? Find(string templateId) =>
        All.FirstOrDefault(t => t.TemplateId == templateId);
}
