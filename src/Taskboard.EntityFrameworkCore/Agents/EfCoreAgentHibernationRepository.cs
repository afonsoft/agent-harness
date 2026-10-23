using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Agents;

/// <summary>SPEC-20261020-agent-hibernation RF-001: EF persistence of records.</summary>
public sealed class EfCoreAgentHibernationRepository : IAgentHibernationRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreAgentHibernationRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<AgentHibernationDto?> GetAsync(
        string sessionKey, CancellationToken ct = default) =>
        await _context.AgentHibernations
            .Where(h => h.Id == sessionKey)
            .Select(h => ToDto(h))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<AgentHibernationDto>> ListAsync(
        string? userId = null, CancellationToken ct = default) =>
        await _context.AgentHibernations
            .Where(h => userId == null || h.UserId == userId)
            .OrderByDescending(h => h.HibernatedAtUtc)
            .Select(h => ToDto(h))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <summary>Keys whose session is currently dead — drives the zzz glyph.</summary>
    public async Task<HashSet<string>> ListKeysAsync(CancellationToken ct = default) =>
        (await _context.AgentHibernations
            .Where(h => h.IsHibernated)
            .Select(h => h.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

    public async Task AddAsync(
        string sessionKey, string userId, string cli, string resumeArgvJson,
        string? nativeSessionId, string? cwd, DateTime idleAtUtc, DateTime atUtc,
        CancellationToken ct = default)
    {
        var existing = await _context.AgentHibernations
            .FindAsync([sessionKey], ct).ConfigureAwait(false);
        if (existing is null)
        {
            _context.AgentHibernations.Add(AgentHibernation.Create(
                sessionKey, userId, cli, resumeArgvJson, nativeSessionId, cwd,
                idleAtUtc, atUtc));
        }
        else
        {
            // re-hibernate refreshes the payload; pin + auto-resume count survive.
            existing.MarkHibernated(resumeArgvJson, nativeSessionId, cwd, idleAtUtc, atUtc);
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Upserts a pin-only row for a live session (RF-009).</summary>
    public async Task PinOnlyAsync(
        string sessionKey, string userId, string cli, string? nativeSessionId,
        string? cwd, DateTime atUtc, CancellationToken ct = default)
    {
        var existing = await _context.AgentHibernations
            .FindAsync([sessionKey], ct).ConfigureAwait(false);
        if (existing is null)
        {
            _context.AgentHibernations.Add(AgentHibernation.PinOnly(
                sessionKey, userId, cli, nativeSessionId, cwd, atUtc));
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task RemoveAsync(string sessionKey, CancellationToken ct = default)
    {
        var existing = await _context.AgentHibernations
            .FindAsync([sessionKey], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            _context.AgentHibernations.Remove(existing);
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task SetPinnedAsync(
        string sessionKey, bool pinned, CancellationToken ct = default)
    {
        var existing = await _context.AgentHibernations
            .FindAsync([sessionKey], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.SetPinned(pinned);
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Clears the hibernated flag; bumps AutoResumedCount on auto resume.</summary>
    public async Task MarkResumedAsync(
        string sessionKey, bool auto, CancellationToken ct = default)
    {
        var existing = await _context.AgentHibernations
            .FindAsync([sessionKey], ct).ConfigureAwait(false);
        existing?.MarkResumed(auto);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>mailbox-wake RF-002: stamps the throttle window before a
    /// respawn attempt so failures do not spin.</summary>
    public async Task MarkAutoResumeAttemptAsync(
        string sessionKey, DateTime atUtc, CancellationToken ct = default)
    {
        var existing = await _context.AgentHibernations
            .FindAsync([sessionKey], ct).ConfigureAwait(false);
        existing?.TouchAutoResume(atUtc);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static AgentHibernationDto ToDto(AgentHibernation h) =>
        new(h.SessionKey, h.UserId, h.Cli, h.IsHibernated, h.HibernatedAtUtc,
            h.IdleAtUtc, ReadArgv(h.ResumeArgvJson), h.NativeSessionId, h.Cwd,
            h.AutoResumedCount, h.Pinned, h.LastAutoResumeUtc);

    private static IReadOnlyList<string> ReadArgv(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? [];
}
