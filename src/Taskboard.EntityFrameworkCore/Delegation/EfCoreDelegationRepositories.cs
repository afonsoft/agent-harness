using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Domain.Entities.Delegation;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Delegation;

/// <summary>
/// EF Core store for delegation DAG tasks (SPEC-20261005 RF-001). The
/// entity↔DTO boundary lives here so Contracts never sees domain types —
/// transitions go through the entity's own methods.
/// </summary>
public sealed class EfCoreDelegationTaskRepository : IDelegationTaskRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreDelegationTaskRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<DelegationTaskDto?> GetAsync(string id, CancellationToken ct = default) =>
        ToDtoOrNull(await _context.DelegationTasks.FindAsync([id], ct).ConfigureAwait(false));

    public async Task<IReadOnlyList<DelegationTaskDto>> ListByScopeAsync(
        string scope, int take = 50, CancellationToken ct = default) =>
        (await _context.DelegationTasks
            .Where(t => t.Scope == scope)
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .Select(ToDto)
        .ToList();

    public async Task<IReadOnlyList<DelegationTaskDto>> ListByIdsAsync(
        IReadOnlyCollection<string> ids, string scope, CancellationToken ct = default) =>
        ids.Count == 0
            ? []
            : (await _context.DelegationTasks
                .Where(t => t.Scope == scope && ids.Contains(t.Id))
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(ToDto)
            .ToList();

    public async Task<IReadOnlyList<DelegationTaskDto>> ListOpenAsync(CancellationToken ct = default) =>
        (await _context.DelegationTasks
            .Where(t => t.Status == DelegationTaskStatus.Pending
                || t.Status == DelegationTaskStatus.Ready
                || t.Status == DelegationTaskStatus.Running)
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .Select(ToDto)
        .ToList();

    public async Task<DelegationTaskDto> AddAsync(
        CreateDelegationTaskRequest request, CancellationToken ct = default)
    {
        var entity = DelegationTask.Create(
            request.Prompt,
            request.CliName,
            request.Scope,
            request.WorkspacePath,
            request.DependsOn,
            request.RetryOf,
            request.FanoutGroupId,
            request.UseWorktree,
            request.RepositoryPath,
            request.BaseCommitSha,
            request.Kind);
        _context.DelegationTasks.Add(entity);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<DelegationTaskDto?> MarkReadyAsync(string id, CancellationToken ct = default) =>
        await TransitionAsync(id, t => t.MarkReady(), ct).ConfigureAwait(false);

    public async Task<DelegationTaskDto?> MarkRunningAsync(
        string id, DateTime now, CancellationToken ct = default) =>
        await TransitionAsync(id, t => t.MarkRunning(now), ct).ConfigureAwait(false);

    public async Task<DelegationTaskDto?> FinalizeAsync(
        string id, DelegationTaskStatus terminal, string? detail,
        DateTime now, CancellationToken ct = default)
    {
        return await TransitionAsync(id, t =>
        {
            switch (terminal)
            {
                case DelegationTaskStatus.Done:
                    t.MarkDone(detail, now);
                    break;
                case DelegationTaskStatus.Failed:
                    t.MarkFailed(detail ?? "failed", now);
                    break;
                case DelegationTaskStatus.Stale:
                    t.MarkStale(detail ?? "stale", now);
                    break;
                case DelegationTaskStatus.Cancelled:
                    t.MarkCancelled(detail ?? "cancelled", now);
                    break;
            }
        }, ct).ConfigureAwait(false);
    }

    public async Task<DelegationTaskDto?> HeartbeatAsync(
        string id, DateTime now, CancellationToken ct = default) =>
        await TransitionAsync(id, t => t.Heartbeat(now), ct).ConfigureAwait(false);

    public async Task<DelegationTaskDto?> AttachWorktreeAsync(
        string id, string worktreeRunId, CancellationToken ct = default) =>
        await TransitionAsync(id, t => t.AttachWorktree(worktreeRunId), ct).ConfigureAwait(false);

    private async Task<DelegationTaskDto?> TransitionAsync(
        string id, Action<DelegationTask> transition, CancellationToken ct)
    {
        var entity = await _context.DelegationTasks.FindAsync([id], ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        transition(entity);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    private static DelegationTaskDto? ToDtoOrNull(DelegationTask? t) =>
        t is null ? null : ToDto(t);

    private static DelegationTaskDto ToDto(DelegationTask t) =>
        new(t.Id, t.Scope, t.Prompt, t.CliName, t.DependsOn, t.RetryOf, t.FanoutGroupId,
            t.UseWorktree, t.WorktreeRunId, t.WorkspacePath, t.RepositoryPath, t.BaseCommitSha,
            t.Status, t.ResultSummary, t.Error,
            t.CreatedAt, t.StartedAt, t.FinishedAt, t.LastHeartbeatAt, t.Kind);
}

/// <summary>EF Core store for the agent mailbox (SPEC-20261005 RF-002).</summary>
public sealed class EfCoreAgentMailboxRepository : IAgentMailboxRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreAgentMailboxRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<MailboxMessageDto> AddAsync(
        PostMailboxMessageRequest request, CancellationToken ct = default)
    {
        var entity = AgentMailboxMessage.Create(
            request.Scope, request.FromAgent, request.ToAgent, request.Payload, request.Kind);
        _context.AgentMailboxMessages.Add(entity);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<MailboxMessageDto?> GetAsync(string id, CancellationToken ct = default)
    {
        var message = await _context.AgentMailboxMessages
            .Where(m => m.Id == id)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return message is null ? null : ToDto(message);
    }

    public async Task<IReadOnlyList<MailboxMessageDto>> ListForRecipientAsync(
        string scope, IReadOnlyCollection<string> recipients, int take = 50,
        bool unreadOnly = false, CancellationToken ct = default)
    {
        if (recipients.Count == 0)
        {
            return [];
        }

        var query = _context.AgentMailboxMessages
            .Where(m => m.Scope == scope && recipients.Contains(m.ToAgent));
        if (unreadOnly)
        {
            query = query.Where(m => m.ReadAt == null);
        }

        return (await query
            .OrderBy(m => m.CreatedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false))
            .Select(ToDto)
            .ToList();
    }

    public async Task<IReadOnlyList<MailboxMessageDto>> ListByScopeAsync(
        string scope, int take = 50, CancellationToken ct = default) =>
        (await _context.AgentMailboxMessages
            .Where(m => m.Scope == scope)
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .Select(ToDto)
        .ToList();

    public async Task MarkReadAsync(
        IReadOnlyCollection<string> ids, DateTime readAt, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var messages = await _context.AgentMailboxMessages
            .Where(m => ids.Contains(m.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var message in messages)
        {
            message.MarkRead(readAt);
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static MailboxMessageDto ToDto(AgentMailboxMessage m) =>
        new(m.Id, m.Scope, m.FromAgent, m.ToAgent, m.Kind, m.Payload, m.CreatedAt, m.ReadAt);
}
