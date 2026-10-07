namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261014-chat-git-bar-overview RF-001/RF-002: state of the resolved
/// conversation workspace for the git bar chips + actions.
/// </summary>
public sealed record ChatGitStatusDto(
    /// <summary>Resolved workspace/worktree dir — null when unresolvable.</summary>
    string? Workdir,
    /// <summary><c>true</c> when the workdir is inside a git checkout.</summary>
    bool IsRepo,
    /// <summary>github.com owner/name from <c>remote.origin.url</c> — drives repo link + PR actions.</summary>
    string? RepoSlug,
    /// <summary>Current branch; <c>null</c> when detached.</summary>
    string? Branch,
    /// <summary>Any uncommitted changes (porcelain non-empty).</summary>
    bool Dirty,
    /// <summary>Commits ahead of upstream (<c>git status -sb</c>).</summary>
    int Ahead,
    /// <summary>Commits behind upstream.</summary>
    int Behind,
    /// <summary>The workdir is a run worktree — pending changes may auto-commit on create-PR.</summary>
    bool IsWorktree,
    /// <summary>A run is active (queued/running/paused) on the conversation — git ops pause.</summary>
    bool RunActive);

/// <summary>Result of a pull/push invocation — streamed into the bar log.</summary>
public sealed record ChatGitOpResult(
    bool Ok,
    string Output,
    /// <summary>Machine-readable refusal: <c>not-a-repo</c>, <c>run-active</c>, <c>git-failed</c>.</summary>
    string? Error);

/// <summary>
/// RF-003: outcome of create-PR — <see cref="UncommittedFiles"/> lists the
/// files blocking a plain workspace (409 <c>uncommitted-changes</c>).
/// </summary>
public sealed record ChatCreatePrResult(
    bool Ok,
    /// <summary><c>true</c> when an open PR already existed for head/base (idempotent).</summary>
    bool Existing,
    int? Number,
    string? Url,
    /// <summary><c>uncommitted-changes</c>, <c>not-a-repo</c>, <c>no-head</c>, <c>slug-unresolved</c>, <c>github-failed</c>, <c>run-active</c>.</summary>
    string? Error,
    IReadOnlyList<string>? UncommittedFiles = null);

/// <summary>RF-003 request body — empty fields fall back to the run summary draft.</summary>
public sealed record CreateChatPullRequestRequest(string? Title = null, string? Body = null);

/// <summary>
/// RF-006: branch picker payload — the checked-out branch plus every
/// switchable branch (locals first, then remote-only entries DWIM'd by
/// <c>git switch</c>).
/// </summary>
public sealed record ChatGitBranchesDto(
    /// <summary>Current branch; <c>null</c> when detached.</summary>
    string? Current,
    /// <summary>All branch names the picker offers (deduped, local + remote).</summary>
    IReadOnlyList<string> Branches);

/// <summary>RF-006 request body — branch to switch to (followed by a pull).</summary>
public sealed record ChatGitCheckoutRequest(string Branch);

/// <summary>RF-004: hover card payload for a <c>github.com/*/pull/N</c> link.</summary>
public sealed record ChatPrCardDto(
    int Number,
    string Url,
    string Title,
    /// <summary><c>open</c>/<c>closed</c>.</summary>
    string State,
    bool Merged,
    string? Author,
    int ChecksTotal,
    int ChecksSucceeded,
    int ChecksFailed);
