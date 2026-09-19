namespace Taskboard.Integrations.Harness;

/// <summary>
/// Runs git commands with structured results. Arguments go through
/// <c>ProcessStartInfo.ArgumentList</c> — never shell-interpolated
/// (SPEC-20260919-harness-workspace-isolation guardrail).
/// </summary>
public interface IGitCommandRunner
{
    Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
