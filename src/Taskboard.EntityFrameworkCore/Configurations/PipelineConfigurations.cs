using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities.Harness;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Taskboard.Harness;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class PipelineExecutionConfiguration : IEntityTypeConfiguration<PipelineExecution>
{
    public void Configure(EntityTypeBuilder<PipelineExecution> builder)
    {
        builder.ToTable("PipelineExecutions");

        builder.Property(e => e.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<PipelineExecutionId>());

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TemplateId).IsRequired().HasMaxLength(128);
        builder.Property(e => e.RepositoryFullName).IsRequired().HasMaxLength(512);
        builder.Property(e => e.RepositoryPath).IsRequired().HasMaxLength(1024);
        builder.Property(e => e.BaseBranch).IsRequired().HasMaxLength(256);
        builder.Property(e => e.IssueId).HasMaxLength(64);
        builder.Property(e => e.InitialPrompt).IsRequired().HasMaxLength(16384);
        builder.Property(e => e.WorktreePath).HasMaxLength(1024);
        builder.Property(e => e.BudgetCapUsd).HasPrecision(18, 6);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(32)
            .HasConversion<string>();

        builder.Property(e => e.CreatedAtUtc);
        builder.Property(e => e.CompletedAtUtc);

        builder.HasMany(e => e.Stages)
            .WithOne()
            .HasForeignKey(s => s.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Stages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(e => e.Version)
            .IsConcurrencyToken();
    }
}

public sealed class PipelineStageExecutionConfiguration : IEntityTypeConfiguration<PipelineStageExecution>
{
    public void Configure(EntityTypeBuilder<PipelineStageExecution> builder)
    {
        builder.ToTable("PipelineStageExecutions");

        builder.Property(s => s.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<PipelineStageExecutionId>());

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ExecutionId)
            .IsRequired()
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<PipelineExecutionId>());

        builder.Property(s => s.StageKey).IsRequired().HasMaxLength(64);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(128);

        builder.Property(s => s.Kind).IsRequired().HasMaxLength(32).HasConversion<string>();
        builder.Property(s => s.Role).HasMaxLength(32).HasConversion<string>();
        builder.Property(s => s.Agent).HasMaxLength(32).HasConversion<string>();
        builder.Property(s => s.ModelTier).IsRequired().HasMaxLength(32).HasConversion<string>();
        builder.Property(s => s.Status).IsRequired().HasMaxLength(32).HasConversion<string>();

        builder.Property(s => s.DependsOn)
            .HasMaxLength(1024)
            .HasConversion(new ReadOnlyListStringJsonValueConverter());

        builder.Property(s => s.TriedAgents)
            .HasMaxLength(1024)
            .HasConversion(new ReadOnlyListStringJsonValueConverter());

        builder.Property(s => s.AdjustedPrompt).HasMaxLength(16384);
        builder.Property(s => s.HandoffSummary).HasMaxLength(16384);
        builder.Property(s => s.ApprovalComment).HasMaxLength(4096);
        builder.Property(s => s.LastError).HasMaxLength(2048);

        builder.Property(s => s.Attempts);
        builder.Property(s => s.StartedAtUtc);
        builder.Property(s => s.CompletedAtUtc);

        builder.HasIndex(s => new { s.ExecutionId, s.StageKey })
            .IsUnique();
    }
}
