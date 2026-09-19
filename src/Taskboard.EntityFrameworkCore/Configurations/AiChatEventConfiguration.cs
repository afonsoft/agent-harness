using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Taskboard.ValueObjects;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class AiChatEventConfiguration : IEntityTypeConfiguration<AiChatEvent>
{
    public void Configure(EntityTypeBuilder<AiChatEvent> builder)
    {
        builder.ToTable("AiChatEvents");

        builder.Property(e => e.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<AiChatEventId>());

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ThreadId)
            .IsRequired()
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<AiChatThreadId>());

        builder.Property(e => e.Role)
            .IsRequired()
            .HasMaxLength(32)
            .HasConversion(new StringValueObjectConverter<AiChatEventRole>());

        builder.Property(e => e.Kind)
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValue(AiChatEventKind.Message)
            .HasConversion(new StringValueObjectConverter<AiChatEventKind>());

        builder.Property(e => e.PayloadJson);

        builder.Property(e => e.Content)
            .IsRequired();

        builder.Property(e => e.CreatedAt);
    }
}
