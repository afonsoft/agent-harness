using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Taskboard.ValueObjects;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class AiChatThreadConfiguration : IEntityTypeConfiguration<AiChatThread>
{
    public void Configure(EntityTypeBuilder<AiChatThread> builder)
    {
        builder.ToTable("AiChatThreads");

        builder.Property(t => t.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<AiChatThreadId>());

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(240);

        builder.Property(t => t.Model)
            .IsRequired()
            .HasMaxLength(128)
            .HasConversion(new StringValueObjectConverter<ModelRef>());

        builder.Property(t => t.ReasoningEffort)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(t => t.Sandbox)
            .IsRequired()
            .HasMaxLength(32)
            .HasConversion(new StringValueObjectConverter<Sandbox>());

        builder.Property(t => t.Status)
            .IsRequired()
            .HasMaxLength(32)
            .HasConversion(new StringValueObjectConverter<AiChatThreadStatus>());

        builder.Property(t => t.Mode)
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValue("assistant");

        builder.Property(t => t.AgentType)
            .HasMaxLength(32)
            .HasConversion<string>();

        builder.Property(t => t.WorkspacePath)
            .HasMaxLength(1024);

        builder.Property(t => t.RepositoryFullName)
            .HasMaxLength(256);

        builder.Property(t => t.ModelTier)
            .HasMaxLength(32);

        builder.Property(t => t.ModelSource)
            .HasMaxLength(32);

        builder.Property(t => t.CreatedAt);
        builder.Property(t => t.UpdatedAt);

        builder.Property(t => t.Version)
            .IsConcurrencyToken();

        builder.HasMany(t => t.Runs)
            .WithOne()
            .HasForeignKey(r => r.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Events)
            .WithOne()
            .HasForeignKey(e => e.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
