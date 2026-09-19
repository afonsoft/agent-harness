using Taskboard.Harness;

namespace Taskboard.Dtos;

/// <summary>Classifier output — risk level + human-readable reason (SPEC RF-001).</summary>
public sealed record CommandRiskAssessment(SecurityRiskLevel RiskLevel, string Reason);

/// <summary>
/// Gateway decision returned by <c>POST /api/harness/security/evaluate</c> (SPEC §5).
/// </summary>
public sealed record SecurityEvaluationDto(
    bool Allowed,
    string RiskLevel,
    bool RequiresApproval,
    string Reason);

/// <summary>Payload for <c>POST /api/harness/security/evaluate</c> (SPEC §5).</summary>
public sealed record SecurityEvaluateRequestDto(
    string ToolName,
    string Command,
    string WorktreePath,
    string? Policy = null);
