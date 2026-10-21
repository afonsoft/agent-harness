using Taskboard.Application.Contracts.Harness;
using Taskboard.Harness;

namespace Taskboard.Application.Harness;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-006/RF-007: card writes — status
/// <c>unknown</c>/null derives via <see cref="IWorktreeStatusProbe"/>; the repo
/// path resolves from the worktree's git-common-dir when missing. Pushes a
/// <c>worktree.card</c> patch after every write (best-effort).
/// </summary>
public sealed class WorktreeCardService : IWorktreeCardService
{
    private readonly IWorktreeCardRepository _cards;
    private readonly IWorktreeStatusProbe _probe;
    private readonly IWorktreeCardNotifier? _notifier;

    public WorktreeCardService(
        IWorktreeCardRepository cards,
        IWorktreeStatusProbe probe,
        IWorktreeCardNotifier? notifier = null)
    {
        _cards = cards;
        _probe = probe;
        _notifier = notifier;
    }

    public async Task<WorktreeCardDto> SetCardAsync(
        string worktreePath,
        string? comment,
        string? status,
        string? taskId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(worktreePath))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue, "worktreePath is required.");
        }

        var resolved = status is null or "unknown" or ""
            ? await _probe.ProbeAsync(worktreePath, cancellationToken)
                .ConfigureAwait(false)
            : ParseStatus(status);

        var repoPath = await _probe.ResolveRepoPathAsync(worktreePath, cancellationToken)
            .ConfigureAwait(false);
        if (repoPath is null)
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "worktreePath is not inside a git checkout.");
        }

        var card = await _cards.UpsertAsync(
            worktreePath.Trim(), repoPath, comment, resolved, taskId,
            cancellationToken).ConfigureAwait(false);
        await NotifyAsync(card, cancellationToken).ConfigureAwait(false);
        return card;
    }

    /// <inheritdoc />
    public async Task<WorktreeCardDto> SetSetupAsync(
        string worktreePath,
        string repoPath,
        string state,
        int? exitCode,
        string? logTail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(worktreePath) || string.IsNullOrWhiteSpace(repoPath))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                "worktreePath and repoPath are required.");
        }

        var card = await _cards.SetSetupAsync(
            worktreePath.Trim(), repoPath.Trim(), state, exitCode, logTail,
            cancellationToken).ConfigureAwait(false);
        await NotifyAsync(card, cancellationToken).ConfigureAwait(false);
        return card;
    }

    /// <summary>Push is best-effort — the write already succeeded.</summary>
    private async Task NotifyAsync(WorktreeCardDto card, CancellationToken ct)
    {
        if (_notifier is null)
        {
            return;
        }

        try
        {
            await _notifier.PushAsync(card, ct).ConfigureAwait(false);
        }
        catch
        {
            // swallow — card row is the source of truth
        }
    }

    public Task<WorktreeCardDto?> GetAsync(
        string worktreePath, CancellationToken cancellationToken = default) =>
        _cards.GetAsync(worktreePath, cancellationToken);

    public Task<IReadOnlyList<WorktreeCardDto>> ListAsync(
        string? repoPath = null, CancellationToken cancellationToken = default) =>
        _cards.ListAsync(repoPath, cancellationToken);

    private static WorkspaceStatus ParseStatus(string status) =>
        status.Trim().ToLowerInvariant() switch
        {
            "clean" => WorkspaceStatus.Clean,
            "dirty" => WorkspaceStatus.Dirty,
            "conflict" => WorkspaceStatus.Conflict,
            "unknown" => WorkspaceStatus.Unknown,
            _ => throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"status must be clean|dirty|conflict|unknown — got '{status}'."),
        };
}
