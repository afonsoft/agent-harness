using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Harness;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class WorktreeCardConfiguration : IEntityTypeConfiguration<WorktreeCard>
{
    public void Configure(EntityTypeBuilder<WorktreeCard> builder)
    {
        builder.ToTable("WorktreeCards");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.WorktreePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.HasIndex(c => c.WorktreePath)
            .IsUnique();

        builder.Property(c => c.RepoPath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.HasIndex(c => c.RepoPath);

        builder.Property(c => c.Comment)
            .HasMaxLength(WorktreeCard.CommentMaxLength);

        builder.Property(c => c.WorkspaceStatus)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion<string>();

        builder.Property(c => c.TaskId)
            .HasMaxLength(128);

        builder.Property(c => c.StatusCommentedAtUtc);

        builder.Property(c => c.SetupState)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(c => c.SetupExitCode);
        builder.Property(c => c.SetupLog);
        builder.Property(c => c.SetupStartedAtUtc);
        builder.Property(c => c.SetupFinishedAtUtc);

        builder.Property(c => c.CreatedAt);
        builder.Property(c => c.UpdatedAt);
    }
}
