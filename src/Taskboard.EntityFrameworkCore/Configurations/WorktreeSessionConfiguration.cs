using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities.Harness;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Taskboard.Harness;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class WorktreeSessionConfiguration : IEntityTypeConfiguration<WorktreeSession>
{
    public void Configure(EntityTypeBuilder<WorktreeSession> builder)
    {
        builder.ToTable("WorktreeSessions");

        builder.Property(s => s.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<WorktreeSessionId>());

        builder.HasKey(s => s.Id);

        builder.Property(s => s.RunId)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(s => s.RunId)
            .IsUnique();

        builder.Property(s => s.RepositoryPath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(s => s.BaseBranch)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.Path)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(s => s.Branch)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.Status)
            .IsRequired()
            .HasMaxLength(32)
            .HasConversion<string>();

        builder.Property(s => s.CommitSha)
            .HasMaxLength(64);

        builder.Property(s => s.RetainOnFailure);

        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.UpdatedAt);

        builder.Property(s => s.Version)
            .IsConcurrencyToken();
    }
}
