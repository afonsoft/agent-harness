using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities.Delegation;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class DelegationTaskConfiguration : IEntityTypeConfiguration<DelegationTask>
{
    public void Configure(EntityTypeBuilder<DelegationTask> builder)
    {
        builder.ToTable("DelegationTasks");

        builder.Property(t => t.Id).HasMaxLength(96);
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Scope).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Prompt).IsRequired().HasMaxLength(16000);
        builder.Property(t => t.CliName).IsRequired().HasMaxLength(128);
        builder.Property(t => t.DependsOnJson).IsRequired().HasColumnName("DependsOn").HasMaxLength(4096);
        builder.Ignore(t => t.DependsOn);

        builder.Property(t => t.RetryOf).HasMaxLength(96);
        builder.Property(t => t.FanoutGroupId).HasMaxLength(96);
        builder.Property(t => t.Kind).IsRequired().HasMaxLength(32);
        builder.Property(t => t.WorktreeRunId).HasMaxLength(96);
        builder.Property(t => t.WorkspacePath).IsRequired().HasMaxLength(512);
        builder.Property(t => t.RepositoryPath).HasMaxLength(512);
        builder.Property(t => t.BaseCommitSha).HasMaxLength(64);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(t => t.ResultSummary).HasMaxLength(4096);
        builder.Property(t => t.Error).HasMaxLength(4096);

        builder.HasIndex(t => new { t.Scope, t.CreatedAt });
        builder.HasIndex(t => new { t.Status });
        builder.HasIndex(t => t.FanoutGroupId);
    }
}
