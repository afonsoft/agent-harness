using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class ConfigurationOverrideConfiguration : IEntityTypeConfiguration<ConfigurationOverride>
{
    public void Configure(EntityTypeBuilder<ConfigurationOverride> builder)
    {
        builder.ToTable("ConfigurationOverrides");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(x => x.Key)
            .IsUnique();

        builder.Property(x => x.Value)
            .IsRequired()
            .HasMaxLength(4096);

        builder.Property(x => x.UpdatedAt)
            .IsRequired();
    }
}
