using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Agents;

/// <summary>SPEC-20261020-agent-attention-model RF-001 — EF store behind the DTO boundary.</summary>
public sealed class EfCoreAgentSessionStateRepository : IAgentSessionStateRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreAgentSessionStateRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<AgentSessionStateDto?> GetAsync(string sessionKey, CancellationToken ct = default) =>
        ToDtoOrNull(await _context.AgentSessionStates
            .FindAsync([sessionKey], ct).ConfigureAwait(false));

    public async Task<IReadOnlyList<AgentSessionStateDto>> ListAsync(
        IReadOnlyCollection<string>? keys = null, int take = 200, CancellationToken ct = default)
    {
        var query = _context.AgentSessionStates.AsQueryable();
        if (keys is { Count: > 0 })
        {
            query = query.Where(s => keys.Contains(s.Id));
        }

        return (await query
            .OrderByDescending(s => s.LastSignalAtUtc)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false))
            .Select(ToDto)
            .ToList();
    }

    public async Task EnsureAsync(
        string sessionKey, string cli, DateTime atUtc, CancellationToken ct = default)
    {
        if (await _context.AgentSessionStates
                .AnyAsync(s => s.Id == sessionKey, ct).ConfigureAwait(false))
        {
            return;
        }

        _context.AgentSessionStates.Add(AgentSessionState.Create(sessionKey, cli, atUtc));
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<(AgentSessionStateDto State, bool Changed)> ApplyAsync(
        AttentionSignal signal, AgentAttentionState next, int weakSignalCount,
        bool resetWeak, DateTime atUtc, CancellationToken ct = default)
    {
        var entity = await _context.AgentSessionStates
            .FindAsync([signal.SessionKey], ct).ConfigureAwait(false);
        if (entity is null)
        {
            entity = AgentSessionState.Create(signal.SessionKey, signal.Cli, atUtc);
            _context.AgentSessionStates.Add(entity);
        }

        var prior = entity.State;
        entity.ApplyTransition(next, signal.Kind, atUtc, signal.Reason, signal.Evidence);
        if (resetWeak)
        {
            entity.ResetWeakSignals();
        }
        else
        {
            entity.SetWeakSignals(weakSignalCount);
        }

        var changed = prior != entity.State;
        if (changed)
        {
            _context.AgentSessionStateTransitions.Add(
                AgentSessionStateTransition.Create(
                    signal.SessionKey, prior, entity.State, signal.Kind,
                    signal.Reason, atUtc));
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return (ToDto(entity), changed);
    }

    public async Task<IReadOnlyList<AgentSessionStateDto>> ListSilentAsync(
        DateTime beforeUtc, int take = 100, CancellationToken ct = default) =>
        (await _context.AgentSessionStates
            .Where(s => s.LastSignalAtUtc < beforeUtc)
            .OrderBy(s => s.LastSignalAtUtc)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .Select(ToDto)
        .ToList();

    public async Task<IReadOnlyList<AgentSessionStateTransitionDto>> ListTransitionsAsync(
        string sessionKey, int take = 100, CancellationToken ct = default) =>
        (await _context.AgentSessionStateTransitions
            .Where(t => t.SessionKey == sessionKey)
            .OrderByDescending(t => t.AtUtc)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .Select(t => new AgentSessionStateTransitionDto(
            t.SessionKey, t.FromState, t.ToState, t.SignalKind, t.Reason, t.AtUtc))
        .ToList();

    private static AgentSessionStateDto? ToDtoOrNull(AgentSessionState? s) =>
        s is null ? null : ToDto(s);

    private static AgentSessionStateDto ToDto(AgentSessionState s) =>
        new(s.SessionKey, s.Cli, s.State, s.AttentionReason, s.StateChangedAtUtc,
            s.LastSignalAtUtc, s.LastSignalKind, s.Evidence, s.Version, s.WeakSignalCount);
}
