using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class AgentCliDefinitionConfiguration : IEntityTypeConfiguration<AgentCliDefinition>
{
    public void Configure(EntityTypeBuilder<AgentCliDefinition> builder)
    {
        builder.ToTable("AgentCliDefinitions");

        builder.Property(d => d.Id)
            .HasMaxLength(96);

        builder.HasKey(d => d.Id);

        builder.Property(d => d.DisplayName)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(d => d.DisplayName)
            .IsUnique();

        builder.Property(d => d.Executable)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(d => d.ArgsTemplate)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(d => d.Transport)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(d => d.ModelFlag)
            .HasMaxLength(64);

        builder.Property(d => d.PromptDelivery)
            .IsRequired()
            .HasMaxLength(16)
            .HasDefaultValue("argv");

        builder.Property(d => d.ModelListArgs)
            .HasMaxLength(256);

        builder.Property(d => d.VersionArgs)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(d => d.Enabled);
        builder.Property(d => d.CreatedAt);
        builder.Property(d => d.UpdatedAt);
    }
}
