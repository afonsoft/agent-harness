using Microsoft.Extensions.Logging;
using Taskboard;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Harness;

namespace Taskboard.Integrations.Harness;

/// <summary>
/// <see cref="IWorkspaceIsolationService"/> backed by native <c>git worktree</c>
/// over <see cref="IGitCommandRunner"/> — sessions persisted via
/// <see cref="IWorktreeSessionRepository"/> (SPEC-20260919-harness-workspace-isolation).
/// </summary>
public sealed class GitWorktreeManager : IWorkspaceIsolationService
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);

    private readonly IGitCommandRunner _git;
    private readonly IWorktreeSessionRepository _sessions;
    private readonly string _worktreeRoot;
    private readonly ILogger<GitWorktreeManager> _logger;

    public GitWorktreeManager(
        IGitCommandRunner git,
        IWorktreeSessionRepository sessions,
        string worktreeRoot,
        ILogger<GitWorktreeManager> logger)
    {
        _git = git;
        _sessions = sessions;
        _worktreeRoot = worktreeRoot;
        _logger = logger;
    }

    public async Task<WorktreeSessionDto> CreateWorktreeAsync(
        string runId,
        string repositoryPath,
        string baseBranch,
        string taskSlug,
        bool retainOnFailure = false,
        CancellationToken cancellationToken = default)
    {
        var existing = await _sessions.GetByRunIdAsync(runId, cancellationToken);
        if (existing is not null && Directory.Exists(existing.Path))
        {
            return existing;
        }

        var path = WorktreePaths.SessionDir(_worktreeRoot, runId);
        if (!WorktreePaths.IsUnder(_worktreeRoot, path))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Worktree path escapes the approved root.");
        }

        if (Directory.Exists(path))
        {
            _logger.LogInformation("Cleaning stale worktree directory {Path}.", path);
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(_worktreeRoot);

        var branch = await AddWorktreeWithUniqueBranchAsync(repositoryPath, path, baseBranch, runId, taskSlug, cancellationToken);

        var session = await _sessions.CreateAsync(
            runId,
            repositoryPath,
            baseBranch,
            path,
            branch,
            retainOnFailure,
            cancellationToken);

        _logger.LogInformation("Worktree {Path} on branch {Branch} created for run {RunId}.", path, branch, runId);
        return session;
    }

    public async Task<WorktreeSessionDto?> GetAsync(string runId, CancellationToken cancellationToken = default)
        => await _sessions.GetByRunIdAsync(runId, cancellationToken);

    public async Task<WorkspaceDiffDto> GetDiffAsync(string runId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken);

        var status = await _git.RunAsync(session.Path, ["status", "--porcelain"], GitTimeout, cancellationToken);
        EnsureSuccess(status, "git status");

        // Two-dot diff vs the base branch covers committed AND working-tree
        // (pending) changes — RF-002 exige ambos.
        var numstat = await _git.RunAsync(session.Path, ["diff", "--numstat", session.BaseBranch], GitTimeout, cancellationToken);
        EnsureSuccess(numstat, "git diff --numstat");

        var patch = await _git.RunAsync(session.Path, ["diff", session.BaseBranch], GitTimeout, cancellationToken);
        EnsureSuccess(patch, "git diff");

        var files = ParseStatus(status.StandardOutput);
        var (insertions, deletions) = ParseNumstat(numstat.StandardOutput);

        return new WorkspaceDiffDto(files.Count, insertions, deletions, files, patch.StandardOutput);
    }

    public async Task<string> CommitAsync(
        string runId,
        string message,
        string author,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken);

        var add = await _git.RunAsync(session.Path, ["add", "-A"], GitTimeout, cancellationToken);
        EnsureSuccess(add, "git add");

        var commit = await _git.RunAsync(
            session.Path,
            ["commit", "-m", message, "--author", author],
            GitTimeout,
            cancellationToken);
        EnsureSuccess(commit, "git commit");

        var revParse = await _git.RunAsync(session.Path, ["rev-parse", "HEAD"], GitTimeout, cancellationToken);
        EnsureSuccess(revParse, "git rev-parse");

        var sha = revParse.StandardOutput.Trim();
        await _sessions.RecordCommitAsync(runId, sha, cancellationToken);
        return sha;
    }

    /// <inheritdoc />
    public async Task<string> PushAsync(string runId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken);
        var push = await _git.RunAsync(
            session.Path,
            ["push", "-u", "origin", session.Branch],
            GitTimeout,
            cancellationToken);
        EnsureSuccess(push, "git push");
        _logger.LogInformation("Run {RunId}: branch {Branch} pushed to origin.", runId, session.Branch);
        return session.Branch;
    }

    public async Task RemoveWorktreeAsync(string runId, bool force = false, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken);

        var args = force
            ? new List<string> { "worktree", "remove", "--force", session.Path }
            : new List<string> { "worktree", "remove", session.Path };

        var remove = await _git.RunAsync(session.RepositoryPath, args, GitTimeout, cancellationToken);
        if (remove.ExitCode != 0)
        {
            _logger.LogWarning("git worktree remove failed ({ExitCode}); pruning and deleting directory.", remove.ExitCode);
            await _git.RunAsync(session.RepositoryPath, ["worktree", "prune"], GitTimeout, cancellationToken);
            if (Directory.Exists(session.Path))
            {
                Directory.Delete(session.Path, recursive: true);
            }
        }

        await _sessions.SetStatusAsync(runId, WorktreeStatus.Removed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(string runId, CancellationToken cancellationToken = default)
    {
        _ = await RequireSessionAsync(runId, cancellationToken);
        await _sessions.SetStatusAsync(runId, WorktreeStatus.Completed, cancellationToken);
    }

    /// <summary>Marks a run's worktree failed; honours <c>RetainOnFailure</c> (RF-004).</summary>
    public async Task MarkFailedAsync(string runId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(runId, cancellationToken);
        await _sessions.SetStatusAsync(runId, WorktreeStatus.Failed, cancellationToken);
        if (session.RetainOnFailure)
        {
            await _sessions.SetStatusAsync(runId, WorktreeStatus.RetainedForInspection, cancellationToken);
        }
    }

    private async Task<string> AddWorktreeWithUniqueBranchAsync(
        string repositoryPath,
        string path,
        string baseBranch,
        string runId,
        string taskSlug,
        CancellationToken cancellationToken)
    {
        var baseName = WorktreePaths.BranchName(runId, taskSlug);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var branch = attempt == 0 ? baseName : $"{baseName}-{attempt + 1}";
            var result = await _git.RunAsync(
                repositoryPath,
                ["worktree", "add", "-b", branch, path, baseBranch],
                GitTimeout,
                cancellationToken);

            if (result.ExitCode == 0)
            {
                return branch;
            }

            if (!result.StandardError.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException(
                    TaskboardDomainErrorCodes.InvalidValue,
                    $"git worktree add failed: {result.StandardError.Trim()}");
            }
        }

        throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Could not allocate a unique worktree branch.");
    }

    private async Task<WorktreeSessionDto> RequireSessionAsync(string runId, CancellationToken cancellationToken)
        => await _sessions.GetByRunIdAsync(runId, cancellationToken)
            ?? throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"No worktree session for run '{runId}'.");

    private static void EnsureSuccess(GitCommandResult result, string operation)
    {
        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"{operation} failed{(result.TimedOut ? " (timeout)" : "")}: {result.StandardError.Trim()}");
        }
    }

    private static List<WorkspaceDiffFileDto> ParseStatus(string porcelain)
    {
        var files = new List<WorkspaceDiffFileDto>();
        foreach (var line in porcelain.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var status = line[..2].Trim() switch
            {
                "??" => "Untracked",
                "M" or "MM" => "Modified",
                "A" => "Added",
                "D" => "Deleted",
                "R" => "Renamed",
                var other => other,
            };

            files.Add(new WorkspaceDiffFileDto(line[3..].Trim(), status));
        }

        return files;
    }

    private static (int Insertions, int Deletions) ParseNumstat(string numstat)
    {
        var insertions = 0;
        var deletions = 0;
        foreach (var line in numstat.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2)
            {
                insertions += int.TryParse(parts[0], out var i) ? i : 0;
                deletions += int.TryParse(parts[1], out var d) ? d : 0;
            }
        }

        return (insertions, deletions);
    }
}
