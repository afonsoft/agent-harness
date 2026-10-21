using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Harness;

namespace Taskboard.Integrations.Harness;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-007: derives workspace status via
/// <c>git status --porcelain</c> — unmerged paths (UU/AA/DD/AU/UA/DU/UD) are
/// <see cref="WorkspaceStatus.Conflict"/>, any other entry
/// <see cref="WorkspaceStatus.Dirty"/>, empty output
/// <see cref="WorkspaceStatus.Clean"/>. Non-zero/failed probes yield
/// <see cref="WorkspaceStatus.Unknown"/>.
/// </summary>
public sealed class GitWorktreeStatusProbe : IWorktreeStatusProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Porcelain status codes that mark an unmerged path.</summary>
    private static readonly HashSet<string> ConflictCodes =
        new(StringComparer.Ordinal) { "DD", "AU", "UD", "UA", "DU", "AA", "UU" };

    private readonly IGitCommandRunner _git;
    private readonly ILogger<GitWorktreeStatusProbe> _logger;

    public GitWorktreeStatusProbe(
        IGitCommandRunner git, ILogger<GitWorktreeStatusProbe> logger)
    {
        _git = git;
        _logger = logger;
    }

    public async Task<WorkspaceStatus> ProbeAsync(
        string worktreePath, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(
            worktreePath, ["status", "--porcelain"], ProbeTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            _logger.LogDebug(
                "git status --porcelain failed for {Path}: {Err}",
                worktreePath, result.StandardError.Trim());
            return WorkspaceStatus.Unknown;
        }

        var sawEntry = false;
        foreach (var line in result.StandardOutput.Split('\n'))
        {
            if (line.Length < 2)
            {
                continue;
            }

            sawEntry = true;
            if (ConflictCodes.Contains(line[..2]))
            {
                return WorkspaceStatus.Conflict;
            }
        }

        return sawEntry ? WorkspaceStatus.Dirty : WorkspaceStatus.Clean;
    }

    public async Task<string?> ResolveRepoPathAsync(
        string worktreePath, CancellationToken cancellationToken = default)
    {
        // `git worktree` shares the main .git via a gitdir pointer —
        // `--git-common-dir` resolves to <main-repo>/.git; its parent is
        // the repo root. Falls back to the worktree's own toplevel for
        // plain checkouts (common-dir == .git → parent == toplevel).
        var result = await _git.RunAsync(
            worktreePath, ["rev-parse", "--path-format=absolute", "--git-common-dir"],
            ProbeTimeout, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var commonDir = result.StandardOutput.Trim();
        return commonDir.EndsWith(".git", StringComparison.Ordinal)
            ? Path.GetDirectoryName(commonDir)
            : Path.GetDirectoryName(commonDir) ?? commonDir;
    }
}
