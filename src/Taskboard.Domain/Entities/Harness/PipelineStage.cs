using Taskboard.Agents;
using Taskboard.Harness;

namespace Taskboard.Domain.Entities.Harness;

/// <summary>
/// Definition-time stage of a pipeline template — what runs, under which role,
/// and which stages must complete first (SPEC-20260919-ade-multi-agent-orchestration RF-001).
/// </summary>
public sealed record PipelineStage(
    string Key,
    string Name,
    PipelineStageKind Kind,
    AgentRole? Role,
    AgentType? Agent,
    AgentModelTier ModelTier,
    IReadOnlyList<string> DependsOn,
    string? InstructionsTemplate = null);
