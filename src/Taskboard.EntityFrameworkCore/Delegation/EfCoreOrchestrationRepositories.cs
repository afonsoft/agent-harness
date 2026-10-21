using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Domain.Entities.Delegation;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Delegation;

/// <summary>
/// EF Core stores of the orchestration contract v2 (SPEC-20261020): runs,
/// dispatch attempts and mailbox delivery batches.
/// </summary>
public sealed class EfCoreDelegationRunRepository : IDelegationRunRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreDelegationRunRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<DelegationRunDto?> GetAsync(string id, CancellationToken ct = default) =>
        ToDtoOrNull(await _context.DelegationRuns.FindAsync([id], ct).ConfigureAwait(false));

    public async Task<DelegationRunDto?> GetOpenByScopeAsync(
        string scope, CancellationToken ct = default) =>
        ToDtoOrNull(await _context.DelegationRuns
            .Where(r => r.Scope == scope && r.Status == DelegationRunStatus.Open)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false));

    public async Task<DelegationRunDto> AddAsync(
        string scope, string objective, string coordinatorKey, CancellationToken ct = default)
    {
        var entity = DelegationRun.Create(scope, objective, coordinatorKey);
        _context.DelegationRuns.Add(entity);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<DelegationRunDto?> SettleAsync(
        string id, DateTime now, CancellationToken ct = default) =>
        await TransitionAsync(id, r => r.Settle(now), ct).ConfigureAwait(false);

    public async Task<DelegationRunDto?> AbortAsync(
        string id, DateTime now, CancellationToken ct = default) =>
        await TransitionAsync(id, r => r.Abort(now), ct).ConfigureAwait(false);

    private async Task<DelegationRunDto?> TransitionAsync(
        string id, Action<DelegationRun> transition, CancellationToken ct)
    {
        var entity = await _context.DelegationRuns.FindAsync([id], ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        transition(entity);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    private static DelegationRunDto? ToDtoOrNull(DelegationRun? r) =>
        r is null ? null : ToDto(r);

    private static DelegationRunDto ToDto(DelegationRun r) =>
        new(r.Id, r.Scope, r.Objective, r.CoordinatorKey, r.Status, r.CreatedAtUtc, r.SettledAtUtc);
}

/// <summary>SPEC-20261020-v2 RF-002: dispatch attempts of delegation tasks.</summary>
public sealed class EfCoreDispatchRepository : IDispatchRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreDispatchRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<DispatchDto?> GetAsync(string id, CancellationToken ct = default) =>
        ToDtoOrNull(await _context.Dispatches.FindAsync([id], ct).ConfigureAwait(false));

    public async Task<IReadOnlyList<DispatchDto>> ListByTaskAsync(
        string taskId, CancellationToken ct = default) =>
        (await _context.Dispatches
            .Where(d => d.TaskId == taskId)
            .OrderBy(d => d.Attempt)
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .Select(ToDto)
        .ToList();

    public async Task<IReadOnlyList<DispatchDto>> ListOpenByRunAsync(
        string runId, CancellationToken ct = default) =>
        (await _context.Dispatches
            .Where(d => d.RunId == runId
                && (d.State == DispatchState.Pending || d.State == DispatchState.Dispatched))
            .OrderBy(d => d.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .Select(ToDto)
        .ToList();

    public async Task<int> LatestAttemptAsync(string taskId, CancellationToken ct = default) =>
        await _context.Dispatches
            .Where(d => d.TaskId == taskId)
            .Select(d => (int?)d.Attempt)
            .MaxAsync(ct)
            .ConfigureAwait(false) ?? 0;

    public async Task<DispatchDto> AddAsync(
        CreateDispatchRequest request, CancellationToken ct = default)
    {
        var entity = Dispatch.Create(
            request.TaskId, request.RunId, request.WorkerKey,
            request.Attempt, request.PreambleJson, id: request.Id);
        _context.Dispatches.Add(entity);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<DispatchDto?> TransitionAsync(
        string id, DispatchState state, string? archivedOutput, DateTime now,
        CancellationToken ct = default)
    {
        var entity = await _context.Dispatches.FindAsync([id], ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        switch (state)
        {
            case DispatchState.Dispatched:
                entity.MarkDispatched(now);
                break;
            case DispatchState.Settled:
                entity.Settle(now);
                break;
            case DispatchState.Retained:
                entity.Retain();
                break;
            case DispatchState.Released:
                entity.Release(archivedOutput, now);
                break;
            case DispatchState.Abandoned:
                entity.Abandon(now);
                break;
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    private static DispatchDto? ToDtoOrNull(Dispatch? d) =>
        d is null ? null : ToDto(d);

    private static DispatchDto ToDto(Dispatch d) =>
        new(d.Id, d.TaskId, d.RunId, d.WorkerKey, d.Attempt, d.State, d.PreambleJson,
            d.TerminalHandle, d.ArchivedOutput,
            d.CreatedAtUtc, d.DispatchedAtUtc, d.SettledAtUtc, d.ReleasedAtUtc);
}

/// <summary>SPEC-20261020-v2 RF-003: FIFO delivery batches of the run inbox.</summary>
public sealed class EfCoreDeliveryRepository : IDeliveryRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreDeliveryRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<DeliveryDto?> GetAsync(string id, CancellationToken ct = default) =>
        ToDtoOrNull(await _context.Deliveries.FindAsync([id], ct).ConfigureAwait(false));

    public async Task<DeliveryDto?> GetOldestUnackedAsync(
        string runId, CancellationToken ct = default) =>
        ToDtoOrNull(await _context.Deliveries
            .Where(d => d.RunId == runId && d.AckedAtUtc == null)
            .OrderBy(d => d.CreatedAtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false));

    public async Task<IReadOnlyList<DeliveryDto>> ListByRunAsync(
        string runId, int take = 50, CancellationToken ct = default) =>
        (await _context.Deliveries
            .Where(d => d.RunId == runId)
            .OrderBy(d => d.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .Select(ToDto)
        .ToList();

    public async Task<DeliveryDto> AddAsync(
        string runId, IReadOnlyList<string> messageIds, CancellationToken ct = default)
    {
        var entity = Delivery.Create(runId, messageIds);
        _context.Deliveries.Add(entity);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<DeliveryDto?> AckAsync(
        string id, DateTime now, CancellationToken ct = default)
    {
        var entity = await _context.Deliveries.FindAsync([id], ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        entity.Ack(now);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    private static DeliveryDto? ToDtoOrNull(Delivery? d) =>
        d is null ? null : ToDto(d);

    private static DeliveryDto ToDto(Delivery d) =>
        new(d.Id, d.RunId, d.MessageIds, d.CreatedAtUtc, d.AckedAtUtc);
}
