using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// SPEC-20261023 RF-C01: per-check outcome for the run preflight doctor.
/// </summary>
public enum PreflightCheckStatus
{
    Ok,
    Warning,
    Blocked,
}

/// <summary>One doctor check: name, status, human message and a fix hint.</summary>
public sealed record PreflightCheck(
    string Name,
    PreflightCheckStatus Status,
    string Message,
    string? NextAction = null);

/// <summary>
/// SPEC-20261023 RF-C01: aggregate verdict — <c>blocked</c> when any check is
/// blocked, <c>warning</c> when any warns, else <c>ready</c>.
/// </summary>
public sealed record PreflightReport(string Verdict, IReadOnlyList<PreflightCheck> Checks)
{
    public static PreflightReport From(IReadOnlyList<PreflightCheck> checks) =>
        new(VerdictOf(checks), checks);

    public static string VerdictOf(IReadOnlyList<PreflightCheck> checks) =>
        checks.Any(c => c.Status is PreflightCheckStatus.Blocked) ? "blocked"
            : checks.Any(c => c.Status is PreflightCheckStatus.Warning) ? "warning"
            : "ready";
}

/// <summary>
/// Pure, side-effect-free preflight: probes agent CLIs, worktree root,
/// MCP servers, chat provider key and disk. Never persists, never spawns
/// beyond the probes themselves (per-check 2s, global 8s bound).
/// </summary>
public interface IPreflightService
{
    Task<PreflightReport> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// SPEC-20261023 RF-C03: effective run configuration resolved server-side —
/// what the run <em>would</em> use (prompt already merged with SpecPath).
/// </summary>
public sealed record ResolvedRunConfig(
    string TemplateId,
    string RepositoryFullName,
    string BaseBranch,
    string? IssueId,
    string Prompt,
    decimal? MaxBudgetUsd,
    AgentType? AgentOverride,
    AgentModelTier? TierOverride,
    bool SkipVerification,
    bool SingleAgent,
    AgentType? SingleAgentType,
    AgentModelTier? SingleAgentTier);

/// <summary>
/// SPEC-20261023 RF-C03: <c>POST /api/harness/runs</c> with
/// <c>dryRun: true</c> — resolved config + preflight report; nothing
/// persisted, nothing spawned.
/// </summary>
public sealed record RunDryRunResult(
    ResolvedRunConfig Resolved,
    string Verdict,
    IReadOnlyList<PreflightCheck> Checks);
