using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.Delegation;

/// <summary>Why a promote+PR request could not create the pull request.</summary>
public enum PromotedLegPrError
{
    /// <summary>
    /// The leg's clone has no resolvable github.com <c>owner/name</c> remote
    /// (task without <c>RepositoryPath</c>, missing session, or non-GitHub
    /// remote). The branch/commit are still pushed — retryable after fixing
    /// the remote.
    /// </summary>
    SlugUnresolved,

    /// <summary>The GitHub API call itself failed (auth, 422, network).</summary>
    GithubFailed,
}

/// <summary>Outcome of <see cref="IPromotedLegPrService.TryCreateAsync"/>.</summary>
public sealed record PromotedLegPrResult(
    string? PullRequestUrl,
    PromotedLegPrError? Error,
    string? Detail = null);

/// <summary>
/// SPEC-20261004-promote-leg-pr: after a promoted leg commits + pushes its
/// branch, optionally opens the GitHub PR <c>branch → base</c>. Extracted from
/// the promote endpoint so the remote→slug resolution and PR call are unit
/// testable without the HTTP layer.
/// </summary>
public interface IPromotedLegPrService
{
    /// <param name="task">The promoted delegation task (needs WorktreeRunId).</param>
    /// <param name="titleOverride">Explicit PR title; defaults to a generated one.</param>
    /// <param name="baseOverride">Explicit base branch; defaults to the session's BaseBranch.</param>
    Task<PromotedLegPrResult> TryCreateAsync(
        DelegationTaskDto task,
        string? titleOverride,
        string? baseOverride,
        CancellationToken cancellationToken = default);
}
