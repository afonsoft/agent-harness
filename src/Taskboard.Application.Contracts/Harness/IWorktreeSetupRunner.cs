using Taskboard.Dtos;
using Taskboard.Harness;

namespace Taskboard.Application.Contracts.Harness;

/// <summary>
/// SPEC-20261021-worktree-setup-scripts RF-001: full parsed view of
/// <c>.harness/worktree.yaml</c> — shared paths, setup/archive commands,
/// startup policy, default tabs and the issue-command template.
/// </summary>
public interface IWorktreeYamlReader
{
    /// <summary>Reads <c>.harness/worktree.yaml</c> under
    /// <paramref name="repositoryPath"/>; missing/malformed files return an
    /// empty doc (malformed fields degrade per-field — a bad
    /// <c>setup</c> entry does not drop the whole file).</summary>
    WorktreeYamlDoc Read(string repositoryPath);

    /// <summary>Effective setup commands — config
    /// <c>Taskboard:Worktree:Setup</c> when non-empty wins over the yaml
    /// <c>setup:</c> list (precedence config &gt; yaml).</summary>
    IReadOnlyList<string> EffectiveSetupCommands(
        string repositoryPath, WorktreeYamlDoc doc);

    /// <summary>Effective startup policy —
    /// <c>Taskboard:Worktree:SetupPolicy</c> &gt;
    /// <c>doc.AgentStartupPolicy</c> &gt; <c>wait-for-setup</c>.</summary>
    string EffectiveStartupPolicy(string repositoryPath, WorktreeYamlDoc doc);

    /// <summary>Effective <c>issueCommand:</c> template —
    /// <c>Taskboard:Worktree:IssueCommand</c> &gt; <c>doc.IssueCommand</c>;
    /// null when neither declares one (RF-007).</summary>
    string? EffectiveIssueCommand(WorktreeYamlDoc doc);
}

/// <summary>
/// SPEC-20261021-worktree-setup-scripts RF-002: runs the worktree
/// <c>setup:</c> command list sequentially inside the new worktree and
/// records the outcome on the worktree card. Commands execute via
/// <c>bash -lc</c> through <c>IProcessRunner</c> with a per-command timeout;
/// captured output is secret-scrubbed and tailed.
/// </summary>
public interface IWorktreeSetupRunner
{
    /// <summary>Run the setup list for a worktree — updates the card
    /// (<c>running</c> → terminal) and returns the outcome for the
    /// caller's preamble/session fields. Never throws on command failure:
    /// a failing command yields <c>failed</c> with its exit code.</summary>
    Task<WorktreeSetupResult> RunSetupAsync(
        string repositoryPath,
        string worktreePath,
        IReadOnlyList<string> commands,
        CancellationToken cancellationToken = default);

    /// <summary>Starts setup in the background (fire-and-forget for
    /// <c>start-immediately</c>) — the card flips
    /// <c>running</c> → terminal when it lands. Returns immediately.</summary>
    void RunSetupInBackground(
        string repositoryPath,
        string worktreePath,
        IReadOnlyList<string> commands);

    /// <summary>
    /// Runs the <c>archive:</c> list inside the worktree before removal —
    /// best-effort teardown: no card transitions, failures are logged and
    /// never abort the delete (RF-005).
    /// </summary>
    Task<WorktreeSetupResult> RunArchiveAsync(
        string repositoryPath,
        string worktreePath,
        IReadOnlyList<string> commands,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// P3 manual rerun: resolves the effective setup commands for the
    /// session's repository, re-runs them (card flips
    /// <c>running</c> → terminal) and propagates the new state + ≤4KB tail
    /// to the session row. Throws <see cref="DomainException"/> when no
    /// setup is declared — shared by the REST endpoint and the
    /// <c>worktree_setup</c> tool.
    /// </summary>
    Task<WorktreeSetupRerunResult> RerunForSessionAsync(
        string runId,
        WorktreeSessionDto session,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a manual setup rerun — the command result plus the
/// session row with refreshed setup fields.</summary>
public sealed record WorktreeSetupRerunResult(
    WorktreeSetupResult Result,
    WorktreeSessionDto Session);

/// <summary>Outcome of a setup run — <see cref="WorktreeSetupStates"/> +
/// tail of scrubbed output (≤64KB) + exit code of the failing command.</summary>
public sealed record WorktreeSetupResult(
    string State,
    int? ExitCode,
    string LogTail,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc);
