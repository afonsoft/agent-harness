using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Agents;

namespace Taskboard.EntityFrameworkCore.Configurations;

/// <summary>SPEC-20261020-agent-attention-model RF-001.</summary>
public sealed class AgentSessionStateConfiguration : IEntityTypeConfiguration<AgentSessionState>
{
    public void Configure(EntityTypeBuilder<AgentSessionState> builder)
    {
        builder.ToTable("AgentSessionStates");

        builder.Property(s => s.Id).HasMaxLength(160);
        builder.HasKey(s => s.Id);
        builder.Ignore(s => s.SessionKey); // alias of Id

        builder.Property(s => s.Cli).IsRequired().HasMaxLength(128);
        builder.Property(s => s.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);
        builder.Property(s => s.AttentionReason).HasMaxLength(256);
        builder.Property(s => s.Evidence).HasMaxLength(256);
        builder.Property(s => s.LastSignalKind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);
        builder.Property(s => s.Version).IsConcurrencyToken();

        builder.HasIndex(s => new { s.State, s.LastSignalAtUtc });
        builder.HasIndex(s => s.LastSignalAtUtc);
    }
}

/// <summary>SPEC-20261020-agent-attention-model RF-001 — append-only ledger.</summary>
public sealed class AgentSessionStateTransitionConfiguration
    : IEntityTypeConfiguration<AgentSessionStateTransition>
{
    public void Configure(EntityTypeBuilder<AgentSessionStateTransition> builder)
    {
        builder.ToTable("AgentSessionStateTransitions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        builder.Property(t => t.SessionKey).IsRequired().HasMaxLength(160);
        builder.Property(t => t.FromState)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);
        builder.Property(t => t.ToState)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);
        builder.Property(t => t.SignalKind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);
        builder.Property(t => t.Reason).HasMaxLength(256);

        builder.HasIndex(t => new { t.SessionKey, t.AtUtc });
    }
}
