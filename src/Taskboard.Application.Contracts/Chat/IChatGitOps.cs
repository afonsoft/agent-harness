namespace Taskboard.Application.Contracts.Chat;

/// <summary>Outcome of a workspace git invocation (SPEC-20261014).</summary>
public sealed record ChatGitCommandResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

/// <summary>
/// SPEC-20261014-chat-git-bar-overview: git + remote-slug operations on a
/// resolved workspace path. Implemented in Taskboard.Integrations over the
/// sandbox-free <c>IGitCommandRunner</c> (env-scrubbed) — keeps the command
/// runner out of the Application layer.
/// </summary>
public interface IChatGitOps
{
    /// <summary>Runs <c>git &lt;args&gt;</c> in <paramref name="workdir"/> with a bounded timeout.</summary>
    Task<ChatGitCommandResult> RunAsync(
        string workdir, IReadOnlyList<string> args, CancellationToken cancellationToken = default);

    /// <summary>
    /// github.com <c>owner/name</c> from <c>git config remote.origin.url</c>
    /// (stored URL — not insteadOf-resolved, so the Devin proxy host can't leak).
    /// </summary>
    Task<string?> ResolveRemoteSlugAsync(string workdir, CancellationToken cancellationToken = default);
}
