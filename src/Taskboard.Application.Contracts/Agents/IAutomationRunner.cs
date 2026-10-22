using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261021-agent-automations RF-003: shared fire path — precheck →
/// launch (delegation task or session inject) → run row → recompute. The
/// AutomationDispatcher calls it for due rows; <see cref="IAutomationService.RunNowAsync"/>
/// calls it for manual fires.
/// </summary>
public interface IAutomationRunner
{
    /// <summary>
    /// Fires one automation now regardless of its schedule; the caller is
    /// responsible for the due/enabled decision. Returns the recorded run
    /// (outcome may be <c>skipped</c> when the precheck fails).
    /// </summary>
    Task<AgentAutomationRunDto> FireAsync(string automationId, CancellationToken ct = default);
}
