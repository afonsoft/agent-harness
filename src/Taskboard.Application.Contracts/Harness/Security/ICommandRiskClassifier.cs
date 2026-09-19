using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// Pre-fork classification of shell commands into risk levels
/// (SPEC-20260919-harness-security-permission-gateway RF-001).
/// Fail-closed: unparseable or unknown commands classify as Dangerous.
/// </summary>
public interface ICommandRiskClassifier
{
    CommandRiskAssessment Classify(string command);
}
