using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Agents;

/// <summary>SPEC-20261020-agent-board-v2 RF-006.</summary>
public sealed class AgentBoardReadMarkerConfiguration : IEntityTypeConfiguration<AgentBoardReadMarker>
{
    public void Configure(EntityTypeBuilder<AgentBoardReadMarker> builder)
    {
        builder.ToTable("AgentBoardReadMarkers");

        builder.Property(m => m.Id).HasMaxLength(320);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.UserId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.CardId).IsRequired().HasMaxLength(160);

        builder.HasIndex(m => m.UserId);
    }
}

/// <summary>EF store for per-user read markers.</summary>
public sealed class EfCoreAgentBoardReadMarkerRepository : IAgentBoardReadMarkerRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreAgentBoardReadMarkerRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyDictionary<string, DateTime>> ListAsync(
        string userId, CancellationToken ct = default) =>
        await _context.AgentBoardReadMarkers
            .Where(m => m.UserId == userId)
            .ToDictionaryAsync(m => m.CardId, m => m.ReadAtUtc, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

    public async Task MarkReadAsync(
        string userId, string cardId, DateTime atUtc, CancellationToken ct = default)
    {
        var key = AgentBoardReadMarker.KeyOf(userId, cardId);
        var marker = await _context.AgentBoardReadMarkers
            .FindAsync([key], ct).ConfigureAwait(false);
        if (marker is null)
        {
            _context.AgentBoardReadMarkers.Add(AgentBoardReadMarker.Create(userId, cardId, atUtc));
        }
        else
        {
            marker.Touch(atUtc);
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
