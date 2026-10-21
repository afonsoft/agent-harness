using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Domain.Entities.Harness;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.Harness;

namespace Taskboard.EntityFrameworkCore.Harness;

/// <inheritdoc cref="IWorktreeCardRepository"/>
public sealed class EfCoreWorktreeCardRepository : IWorktreeCardRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreWorktreeCardRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<WorktreeCardDto> UpsertAsync(
        string worktreePath,
        string repoPath,
        string? comment,
        WorkspaceStatus status,
        string? taskId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var entity = await _context.WorktreeCards
            .FirstOrDefaultAsync(c => c.WorktreePath == worktreePath, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = WorktreeCard.Create(
                worktreePath, repoPath, comment, status, taskId, now);
            await _context.WorktreeCards.AddAsync(entity, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            entity.SetStatus(comment, status, taskId, now);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entity);
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
        var now = DateTime.UtcNow;
        var entity = await _context.WorktreeCards
            .FirstOrDefaultAsync(c => c.WorktreePath == worktreePath, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = WorktreeCard.Create(
                worktreePath, repoPath, null, WorkspaceStatus.Unknown, null, now);
            await _context.WorktreeCards.AddAsync(entity, cancellationToken)
                .ConfigureAwait(false);
        }

        entity.SetSetup(
            state, exitCode,
            logTail is { Length: > MaxSetupLogBytes } tail
                ? tail[^MaxSetupLogBytes..]
                : logTail,
            now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<WorktreeCardDto?> GetAsync(
        string worktreePath, CancellationToken cancellationToken = default)
    {
        var entity = await _context.WorktreeCards
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.WorktreePath == worktreePath, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<WorktreeCardDto>> ListAsync(
        string? repoPath = null, CancellationToken cancellationToken = default)
    {
        var query = _context.WorktreeCards.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(repoPath))
        {
            query = query.Where(c => c.RepoPath == repoPath);
        }

        var entities = await query
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(ToDto).ToList();
    }

    /// <summary>Card keeps a ≤64KB scrubbed tail (RF-003).</summary>
    private const int MaxSetupLogBytes = 64 * 1024;

    /// <summary>DTO tail — enough for the card UI + preamble echo.</summary>
    private const int LogTailBytes = 4 * 1024;

    private static WorktreeCardDto ToDto(WorktreeCard c) => new(
        c.WorktreePath,
        c.RepoPath,
        c.Comment,
        c.WorkspaceStatus,
        c.TaskId,
        c.StatusCommentedAtUtc,
        c.UpdatedAt,
        c.SetupState,
        c.SetupExitCode,
        c.SetupStartedAtUtc,
        c.SetupFinishedAtUtc,
        c.SetupLog is { Length: > LogTailBytes } log
            ? log[^LogTailBytes..]
            : c.SetupLog);
}
