using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities.Delegation;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class AgentMailboxMessageConfiguration : IEntityTypeConfiguration<AgentMailboxMessage>
{
    public void Configure(EntityTypeBuilder<AgentMailboxMessage> builder)
    {
        builder.ToTable("AgentMailboxMessages");

        builder.Property(m => m.Id).HasMaxLength(96);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Scope).IsRequired().HasMaxLength(128);
        builder.Property(m => m.FromAgent).IsRequired().HasMaxLength(128);
        builder.Property(m => m.ToAgent).IsRequired().HasMaxLength(128);
        builder.Property(m => m.Kind).IsRequired().HasMaxLength(24);
        builder.Property(m => m.Payload).IsRequired().HasMaxLength(16000);

        builder.HasIndex(m => new { m.Scope, m.ToAgent, m.ReadAt });
    }
}
