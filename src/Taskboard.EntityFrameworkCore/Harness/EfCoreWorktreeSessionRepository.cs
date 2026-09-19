using Microsoft.EntityFrameworkCore;
using Taskboard;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.Harness;

namespace Taskboard.EntityFrameworkCore.Harness;

public sealed class EfCoreWorktreeSessionRepository : IWorktreeSessionRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreWorktreeSessionRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<WorktreeSessionDto> CreateAsync(
        string runId,
        string repositoryPath,
        string baseBranch,
        string path,
        string branch,
        bool retainOnFailure,
        CancellationToken cancellationToken = default)
    {
        var entity = WorktreeSession.Create(
            WorktreeSessionId.NewGuid(),
            runId,
            repositoryPath,
            baseBranch,
            path,
            branch,
            retainOnFailure);

        await _context.WorktreeSessions.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<WorktreeSessionDto?> GetByRunIdAsync(string runId, CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(runId, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<WorktreeSessionDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.WorktreeSessions
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(ToDto).ToList();
    }

    public async Task<WorktreeSessionDto> RecordCommitAsync(string runId, string commitSha, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(runId, cancellationToken).ConfigureAwait(false);
        entity.RecordCommit(commitSha);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<WorktreeSessionDto> SetStatusAsync(string runId, WorktreeStatus status, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(runId, cancellationToken).ConfigureAwait(false);
        switch (status)
        {
            case WorktreeStatus.Completed:
                entity.MarkCompleted();
                break;
            case WorktreeStatus.Failed:
                entity.MarkFailed();
                break;
            case WorktreeStatus.RetainedForInspection:
                entity.MarkRetainedForInspection();
                break;
            case WorktreeStatus.Removed:
                entity.MarkRemoved();
                break;
            default:
                throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"Invalid worktree status transition to '{status}'.");
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entity);
    }

    private Task<WorktreeSession?> FindAsync(string runId, CancellationToken cancellationToken)
        => _context.WorktreeSessions.FirstOrDefaultAsync(s => s.RunId == runId, cancellationToken);

    private async Task<WorktreeSession> RequireAsync(string runId, CancellationToken cancellationToken)
        => await FindAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, $"No worktree session for run '{runId}'.");

    private static WorktreeSessionDto ToDto(WorktreeSession session)
        => new(
            session.Id.Value,
            session.RunId,
            session.Path,
            session.Branch,
            session.Status.ToString(),
            session.RepositoryPath,
            session.BaseBranch,
            session.CommitSha,
            session.RetainOnFailure,
            session.CreatedAt,
            session.UpdatedAt,
            session.Version);
}
