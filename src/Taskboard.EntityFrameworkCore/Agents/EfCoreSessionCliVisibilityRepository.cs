using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Agents;

/// <summary>SPEC-20261020-session-history-parity RF-005.</summary>
public sealed class EfCoreSessionCliVisibilityRepository : ISessionCliVisibilityRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreSessionCliVisibilityRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<string>> ListHiddenAsync(
        string userId, CancellationToken ct = default) =>
        await _context.SessionCliVisibilities
            .Where(v => v.UserId == userId && v.Hidden)
            .Select(v => v.Cli)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task SetHiddenAsync(
        string userId, string cli, bool hidden, CancellationToken ct = default)
    {
        var key = SessionCliVisibility.KeyOf(userId, cli);
        var entity = await _context.SessionCliVisibilities
            .FindAsync([key], ct).ConfigureAwait(false);
        if (hidden)
        {
            if (entity is null)
            {
                _context.SessionCliVisibilities.Add(
                    SessionCliVisibility.Create(userId, cli, hidden: true));
            }
            else
            {
                entity.SetHidden(true);
            }
        }
        else if (entity is not null)
        {
            _context.SessionCliVisibilities.Remove(entity);
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
