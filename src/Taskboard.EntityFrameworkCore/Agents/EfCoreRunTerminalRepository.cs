using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Agents;

/// <summary>SPEC-20261104-run-terminal-runtime RF-001: run-terminal persistence.</summary>
public sealed class EfCoreRunTerminalRepository(TaskboardDbContext context) : IRunTerminalRepository
{
    public async Task<RunTerminalDto?> GetAsync(string id, CancellationToken ct = default) =>
        ToDtoOrNull(await context.RunTerminals.FindAsync([id], ct).ConfigureAwait(false));

    public async Task<RunTerminalDto?> GetByDispatchAsync(string dispatchId, CancellationToken ct = default) =>
        ToDtoOrNull(await context.RunTerminals
            .Where(r => r.DispatchId == dispatchId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false));

    public async Task<RunTerminalDto?> GetByPtySessionIdAsync(string sessionId, CancellationToken ct = default) =>
        ToDtoOrNull(await context.RunTerminals
            .Where(r => r.PtySessionId == sessionId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false));

    public async Task<RunTerminalDto?> GetLiveByTaskAsync(string taskId, CancellationToken ct = default) =>
        ToDtoOrNull(await context.RunTerminals
            .Where(r => r.TaskId == taskId && r.State == RunTerminalState.Live)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false));

    public async Task<IReadOnlyList<RunTerminalDto>> ListByTaskAsync(string taskId, CancellationToken ct = default) =>
        (await context.RunTerminals
            .Where(r => r.TaskId == taskId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .Select(ToDto)
        .ToList();

    public async Task<IReadOnlyList<RunTerminalDto>> ListLiveAsync(CancellationToken ct = default) =>
        (await context.RunTerminals
            .Where(r => r.State == RunTerminalState.Live)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .Select(ToDto)
        .ToList();

    public async Task<RunTerminalDto> AddAsync(
        EnsureRunTerminalRequest request, string id, string ptySessionId, CancellationToken ct = default)
    {
        var entity = RunTerminal.Create(
            request.TaskId, request.DispatchId, ptySessionId, request.CliName, id: id);
        context.RunTerminals.Add(entity);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<RunTerminalDto?> SetControlOwnerAsync(
        string id, RunTerminalControlOwner owner, CancellationToken ct = default)
    {
        var entity = await context.RunTerminals.FindAsync([id], ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        if (owner == RunTerminalControlOwner.Human)
        {
            entity.TakeHumanControl();
        }
        else
        {
            entity.ReleaseHumanControl();
        }

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    public async Task<RunTerminalDto?> FinishAsync(
        string id, int? exitCode, string? archivedOutput, DateTime now, CancellationToken ct = default)
    {
        var entity = await context.RunTerminals.FindAsync([id], ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        entity.Finish(exitCode, archivedOutput, now);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity);
    }

    private static RunTerminalDto? ToDtoOrNull(RunTerminal? r) =>
        r is null ? null : ToDto(r);

    private static RunTerminalDto ToDto(RunTerminal r) =>
        new(r.Id, r.TaskId, r.DispatchId, r.PtySessionId, r.CliName,
            r.State, r.ControlOwner, Status: null,
            r.CreatedAtUtc, r.FinishedAtUtc, r.ExitCode,
            FirstMessageSent: false, FirstMessageReason: null,
            r.ArchivedOutput);
}
