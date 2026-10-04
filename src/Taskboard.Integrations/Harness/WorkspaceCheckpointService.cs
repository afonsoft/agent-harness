using Taskboard.Application.Contracts.Harness;

namespace Taskboard.Integrations.Harness;

/// <summary>
/// SPEC-20261006 RF-004: checkpoints são commits <c>harness-checkpoint:</c> na
/// branch do worktree — snapshot (add -A + commit), list (log filtrado) e
/// restore (reset --hard). Limite de escopo: só worktrees gerenciados
/// (runId → <see cref="IWorkspaceIsolationService"/>), nunca paths livres.
/// </summary>
public sealed class WorkspaceCheckpointService : IWorkspaceCheckpointService
{
    internal const string Prefix = "harness-checkpoint:";
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);

    private readonly IWorkspaceIsolationService _worktrees;
    private readonly IGitCommandRunner _git;
    private readonly TimeProvider _time;

    public WorkspaceCheckpointService(
        IWorkspaceIsolationService worktrees,
        IGitCommandRunner git,
        TimeProvider? time = null)
    {
        _worktrees = worktrees;
        _git = git;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<WorktreeCheckpointDto> CreateCheckpointAsync(
        string runId, string? label = null, CancellationToken ct = default)
    {
        var path = await RequirePathAsync(runId, ct).ConfigureAwait(false);
        var createdAt = _time.GetUtcNow();
        var effective = string.IsNullOrWhiteSpace(label) ? "checkpoint" : label.Trim();

        var add = await _git.RunAsync(path, ["add", "-A"], GitTimeout, ct).ConfigureAwait(false);
        EnsureSuccess(add, "git add");

        // A clean worktree makes `git commit` exit non-zero ("nothing to
        // commit") — the checkpoint is then the current HEAD, not an error.
        await _git.RunAsync(
            path,
            ["commit", "--no-verify", "-m", $"{Prefix} {effective}", "--author", "Harness <harness@local>"],
            GitTimeout, ct).ConfigureAwait(false);

        var head = await _git.RunAsync(path, ["rev-parse", "HEAD"], GitTimeout, ct).ConfigureAwait(false);
        EnsureSuccess(head, "git rev-parse");

        return new WorktreeCheckpointDto(head.StandardOutput.Trim(), effective, createdAt);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorktreeCheckpointDto>> ListCheckpointsAsync(
        string runId, int take = 50, CancellationToken ct = default)
    {
        var path = await RequirePathAsync(runId, ct).ConfigureAwait(false);
        var log = await _git.RunAsync(
            path, ["log", "--format=%H%x09%cI%x09%s", "-n", "200"], GitTimeout, ct).ConfigureAwait(false);
        EnsureSuccess(log, "git log");

        var checkpoints = new List<WorktreeCheckpointDto>();
        foreach (var line in log.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 3);
            if (parts.Length != 3 || !parts[2].StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var label = parts[2][Prefix.Length..].Trim();
            var when = DateTimeOffset.TryParse(parts[1], out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;
            checkpoints.Add(new WorktreeCheckpointDto(parts[0], label, when));
            if (checkpoints.Count >= take)
            {
                break;
            }
        }

        return checkpoints;
    }

    /// <inheritdoc />
    public async Task<WorktreeCheckpointDto> RestoreCheckpointAsync(
        string runId, string sha, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sha))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "sha is required");
        }

        var path = await RequirePathAsync(runId, ct).ConfigureAwait(false);
        var verify = await _git.RunAsync(
            path, ["rev-parse", "--verify", $"{sha}^{{commit}}"], GitTimeout, ct).ConfigureAwait(false);
        if (verify.ExitCode != 0)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, $"not a commit of this worktree: {sha}");
        }

        var resolved = verify.StandardOutput.Trim();
        var reset = await _git.RunAsync(
            path, ["reset", "--hard", resolved], GitTimeout, ct).ConfigureAwait(false);
        EnsureSuccess(reset, "git reset");

        var known = (await ListCheckpointsAsync(runId, take: 200, ct).ConfigureAwait(false))
            .FirstOrDefault(c => string.Equals(c.Sha, resolved, StringComparison.OrdinalIgnoreCase));
        return known ?? new WorktreeCheckpointDto(resolved, "(restored)", _time.GetUtcNow());
    }

    private async Task<string> RequirePathAsync(string runId, CancellationToken ct)
    {
        var session = await _worktrees.GetAsync(runId, ct).ConfigureAwait(false)
            ?? throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, $"no worktree for run '{runId}'");
        return session.Path;
    }

    private static void EnsureSuccess(GitCommandResult result, string operation)
    {
        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.RepositoryProvisioningFailed,
                $"{operation} failed: {result.StandardError}");
        }
    }
}
