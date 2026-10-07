using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Harness;

namespace Taskboard.Integrations.Chat;

/// <inheritdoc cref="IChatGitOps"/>
public sealed class ChatGitOps(IGitCommandRunner git) : IChatGitOps
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);

    public async Task<ChatGitCommandResult> RunAsync(
        string workdir, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var result = await git.RunAsync(workdir, args, GitTimeout, cancellationToken).ConfigureAwait(false);
        return new ChatGitCommandResult(result.ExitCode, result.StandardOutput, result.StandardError, result.TimedOut);
    }

    public async Task<string?> ResolveRemoteSlugAsync(string workdir, CancellationToken cancellationToken = default)
    {
        // `config remote.origin.url` reads the STORED URL — `remote get-url`
        // resolves url.insteadOf rewrites, which on Devin-proxy machines would
        // break slug resolution (same rule as PromotedLegPrService).
        var remote = await git.RunAsync(
            workdir, ["config", "remote.origin.url"], GitTimeout, cancellationToken).ConfigureAwait(false);
        return remote.ExitCode == 0 ? GitRemoteSlug.TryParse(remote.StandardOutput) : null;
    }
}
