using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities.Harness;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Taskboard.Harness;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class ProjectMemoryItemConfiguration : IEntityTypeConfiguration<ProjectMemoryItem>
{
    public void Configure(EntityTypeBuilder<ProjectMemoryItem> builder)
    {
        builder.ToTable("ProjectMemoryItems");

        builder.Property(m => m.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ProjectMemoryItemId>());

        builder.HasKey(m => m.Id);

        builder.Property(m => m.RepositoryFullName)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(m => m.RepositoryFullName);

        builder.Property(m => m.Topic)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.Content)
            .IsRequired()
            .HasMaxLength(8192);

        builder.Property(m => m.Type)
            .IsRequired()
            .HasMaxLength(32)
            .HasConversion<string>();

        builder.Property(m => m.Tags)
            .HasConversion(new ReadOnlyListStringJsonValueConverter())
            .HasMaxLength(2048);

        builder.Property(m => m.CreatedAt);
        builder.Property(m => m.UpdatedAt);

        builder.Property(m => m.Version)
            .IsConcurrencyToken();
    }
}
