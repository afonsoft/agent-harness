using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Agents;

namespace Taskboard.EntityFrameworkCore.Agents;

public sealed class AgentRunEventConfiguration : IEntityTypeConfiguration<AgentRunEvent>
{
    public void Configure(EntityTypeBuilder<AgentRunEvent> builder)
    {
        builder.ToTable("AgentRunEvents");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ScopeKind).IsRequired().HasMaxLength(32);
        builder.Property(x => x.ScopeId).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Kind).IsRequired().HasMaxLength(64);
        builder.Property(x => x.StageId).HasMaxLength(255);
        builder.Property(x => x.SessionId).HasMaxLength(255);
        builder.Property(x => x.ParentEventId).HasMaxLength(64);
        builder.Property(x => x.ToolCallId).HasMaxLength(255);
        builder.Property(x => x.MessageId).HasMaxLength(255);
        builder.Property(x => x.PlanId).HasMaxLength(255);
        builder.Property(x => x.PatchOp).HasMaxLength(16);
        builder.Property(x => x.Title).HasMaxLength(512);
        builder.Property(x => x.Stream).IsRequired().HasMaxLength(16);
        builder.Property(x => x.TimestampUtc)
            .IsRequired()
            .HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));

        builder.HasIndex(x => new { x.ScopeKind, x.ScopeId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.ScopeId, x.Kind });
        builder.HasIndex(x => x.TimestampUtc);
    }
}
