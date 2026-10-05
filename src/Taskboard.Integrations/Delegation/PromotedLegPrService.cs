using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.GitHub;
using Taskboard.Integrations.Harness;

namespace Taskboard.Integrations.Delegation;

/// <inheritdoc cref="IPromotedLegPrService"/>
public sealed class PromotedLegPrService : IPromotedLegPrService
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(15);

    private readonly IWorkspaceIsolationService _isolation;
    private readonly IGitCommandRunner _git;
    private readonly IGitHubService _github;

    public PromotedLegPrService(
        IWorkspaceIsolationService isolation,
        IGitCommandRunner git,
        IGitHubService github)
    {
        _isolation = isolation;
        _git = git;
        _github = github;
    }

    public async Task<PromotedLegPrResult> TryCreateAsync(
        DelegationTaskDto task,
        string? titleOverride,
        string? baseOverride,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task.RepositoryPath) || task.WorktreeRunId is null)
        {
            return new PromotedLegPrResult(
                null, PromotedLegPrError.SlugUnresolved,
                "task has no repository checkout to derive a remote from");
        }

        var session = await _isolation.GetAsync(task.WorktreeRunId, cancellationToken);
        var repoPath = session?.RepositoryPath ?? task.RepositoryPath;
        var head = session?.Branch;
        if (string.IsNullOrWhiteSpace(head))
        {
            return new PromotedLegPrResult(
                null, PromotedLegPrError.SlugUnresolved,
                "worktree session has no branch to use as PR head");
        }

        var remote = await _git.RunAsync(
            repoPath, ["remote", "get-url", "origin"], GitTimeout, cancellationToken);
        var slug = remote.ExitCode == 0 ? GitRemoteSlug.TryParse(remote.StandardOutput) : null;
        if (slug is null)
        {
            return new PromotedLegPrResult(
                null, PromotedLegPrError.SlugUnresolved,
                "origin remote does not resolve to a github.com owner/name");
        }

        var baseBranch = NormalizeBase(baseOverride)
            ?? NormalizeBase(session?.BaseBranch)
            ?? await OriginHeadAsync(repoPath, cancellationToken)
            ?? "main";

        var title = string.IsNullOrWhiteSpace(titleOverride)
            ? $"delegation: {task.CliName} leg — {Truncate(task.Prompt, 60)}"
            : titleOverride.Trim();
        var body = $"Promoted by Harness delegation.\n\n"
            + $"- task `{task.Id}`\n- cli `{task.CliName}`\n- worktree run `{task.WorktreeRunId}`\n";

        try
        {
            var url = await _github.CreatePullRequestAsync(
                slug, title, head, baseBranch, body, cancellationToken);
            return new PromotedLegPrResult(url, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PromotedLegPrResult(null, PromotedLegPrError.GithubFailed, ex.Message);
        }
    }

    private async Task<string?> OriginHeadAsync(string repoPath, CancellationToken ct)
    {
        var symref = await _git.RunAsync(
            repoPath,
            ["symbolic-ref", "--short", "refs/remotes/origin/HEAD"],
            GitTimeout, ct);
        return symref.ExitCode == 0 ? NormalizeBase(symref.StandardOutput) : null;
    }

    private static string? NormalizeBase(string? baseBranch)
    {
        var value = baseBranch?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        const string remotePrefix = "origin/";
        return value.StartsWith(remotePrefix, StringComparison.Ordinal)
            ? value[remotePrefix.Length..]
            : value;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");
}
