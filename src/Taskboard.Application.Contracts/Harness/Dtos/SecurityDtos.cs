using Taskboard.Harness;

namespace Taskboard.Dtos;

/// <summary>
/// Classifier output — risk level + human-readable reason (SPEC RF-001).
/// <see cref="EscapesSandbox"/> marks jail violations, which are hard-denied
/// (no approval flow can make them safe).
/// </summary>
public sealed record CommandRiskAssessment(
    SecurityRiskLevel RiskLevel,
    string Reason,
    bool EscapesSandbox = false);

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
