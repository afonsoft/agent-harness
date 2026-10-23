namespace Taskboard.Harness;

/// <summary>
/// SPEC-20261021-worktree-setup-scripts RF-003: lifecycle of the
/// <c>.harness/worktree.yaml</c> <c>setup:</c> commands on a worktree card.
/// </summary>
public static class WorktreeSetupStates
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Ok = "ok";
    public const string Failed = "failed";

    /// <summary>No setup declared, or the yaml field was invalid.</summary>
    public const string Skipped = "skipped";

    public static bool IsTerminal(string state) =>
        state is Ok or Failed or Skipped;

    public static bool IsValid(string state) =>
        state is Pending or Running or Ok or Failed or Skipped;
}

/// <summary>
/// SPEC-20261021-worktree-setup-scripts RF-004: whether the worker dispatch
/// waits for setup to finish or starts in parallel.
/// </summary>
public static class WorktreeSetupPolicies
{
    /// <summary>Dispatch only after setup reaches a terminal state.</summary>
    public const string WaitForSetup = "wait-for-setup";

    /// <summary>Worker and setup run in parallel.</summary>
    public const string StartImmediately = "start-immediately";

    public static bool IsValid(string policy) =>
        policy is WaitForSetup or StartImmediately;
}

/// <summary>Declared <c>.harness/worktree.yaml</c> contents (v2 schema).</summary>
public sealed record WorktreeYamlDoc(
    IReadOnlyList<WorktreeYamlSharedEntry> Shared,
    IReadOnlyList<string> Exclude,
    IReadOnlyList<string> Setup,
    IReadOnlyList<string> Archive,
    string? AgentStartupPolicy,
    IReadOnlyList<WorktreeYamlTab> DefaultTabs,
    string? IssueCommand,
    string? WorkerBrief = null);

/// <summary>One <c>shared:</c> entry — raw path kept for validation.</summary>
public sealed record WorktreeYamlSharedEntry(string Path, string? Mode, bool Invalid);

/// <summary>One <c>defaultTabs:</c> entry.</summary>
public sealed record WorktreeYamlTab(string Name, string Command);

/// <summary>Provisioning outcome snapshot persisted on a worktree session or
/// delegation task — which shared paths were materialized plus the setup
/// lifecycle state and its scrubbed log tail (≤4KB).</summary>
public sealed record WorktreeProvisionOutcome(
    IReadOnlyList<string>? SharedPathsApplied,
    string? SetupState,
    string? SetupLogTail);

/// <summary>
/// SPEC-20261021-worktree-setup-scripts RF-007: renders the
/// <c>issueCommand:</c> template into a worker prompt. Known placeholders —
/// <c>{issueUrl}</c>, <c>{issueKey}</c> (<c>owner/repo#42</c>),
/// <c>{issueNumber}</c>, <c>{issueTitle}</c>, <c>{issueBody}</c>,
/// <c>{repo}</c>. A template with <c>{{</c>/<c>}}</c>, <c>$(</c> or leftover
/// <c>{…}</c> placeholders is rejected (returns <c>null</c>) so callers fall
/// back to the default prompt — templates never reach a shell, but malformed
/// output would poison the worker prompt.
/// </summary>
public static class IssueCommandTemplate
{
    public static string? Render(
        string template,
        string repo,
        int issueNumber,
        string issueTitle,
        string? issueBody = null,
        string? issueUrl = null)
    {
        if (string.IsNullOrWhiteSpace(template)
            || template.Contains("{{", StringComparison.Ordinal)
            || template.Contains("}}", StringComparison.Ordinal)
            || template.Contains("$(", StringComparison.Ordinal))
        {
            return null;
        }

        var issueKey = $"{repo}#{issueNumber}";
        var rendered = template
            .Replace("{issueUrl}", issueUrl ?? issueKey, StringComparison.Ordinal)
            .Replace("{issueKey}", issueKey, StringComparison.Ordinal)
            .Replace("{issueNumber}", issueNumber.ToString(), StringComparison.Ordinal)
            .Replace("{issueTitle}", issueTitle, StringComparison.Ordinal)
            .Replace("{issueBody}", issueBody ?? string.Empty, StringComparison.Ordinal)
            .Replace("{repo}", repo, StringComparison.Ordinal);

        // Any remaining {placeholder} is an unknown variable — reject.
        return rendered.IndexOf('{') >= 0 || rendered.IndexOf('}') >= 0
            ? null
            : rendered;
    }
}
