using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities.Delegation;

namespace Taskboard.EntityFrameworkCore.Configurations;

/// <summary>SPEC-20261020-orchestration-contract-v2 RF-001.</summary>
public sealed class DelegationRunConfiguration : IEntityTypeConfiguration<DelegationRun>
{
    public void Configure(EntityTypeBuilder<DelegationRun> builder)
    {
        builder.ToTable("DelegationRuns");

        builder.Property(r => r.Id).HasMaxLength(96);
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Scope).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Objective).IsRequired().HasMaxLength(2048);
        builder.Property(r => r.CoordinatorKey).IsRequired().HasMaxLength(128);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasIndex(r => new { r.Scope, r.Status });
    }
}

/// <summary>SPEC-20261020-orchestration-contract-v2 RF-002.</summary>
public sealed class DispatchConfiguration : IEntityTypeConfiguration<Dispatch>
{
    public void Configure(EntityTypeBuilder<Dispatch> builder)
    {
        builder.ToTable("Dispatches");

        builder.Property(d => d.Id).HasMaxLength(96);
        builder.HasKey(d => d.Id);

        builder.Property(d => d.TaskId).IsRequired().HasMaxLength(96);
        builder.Property(d => d.RunId).IsRequired().HasMaxLength(96);
        builder.Property(d => d.WorkerKey).IsRequired().HasMaxLength(128);
        builder.Property(d => d.PreambleJson).IsRequired().HasMaxLength(16000);
        builder.Property(d => d.TerminalHandle).HasMaxLength(256);
        builder.Property(d => d.ArchivedOutput).HasMaxLength(32768);

        builder.Property(d => d.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasIndex(d => new { d.TaskId, d.Attempt });
        builder.HasIndex(d => new { d.RunId, d.State });
    }
}

/// <summary>SPEC-20261020-orchestration-contract-v2 RF-003.</summary>
public sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("Deliveries");

        builder.Property(d => d.Id).HasMaxLength(96);
        builder.HasKey(d => d.Id);

        builder.Property(d => d.RunId).IsRequired().HasMaxLength(96);
        builder.Property(d => d.MessagesJson).IsRequired().HasColumnName("Messages").HasMaxLength(8192);
        builder.Ignore(d => d.MessageIds);

        builder.HasIndex(d => new { d.RunId, d.AckedAtUtc });
    }
}
