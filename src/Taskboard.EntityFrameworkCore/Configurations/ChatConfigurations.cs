using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities.Chat;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Taskboard.ValueObjects;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class ChatProviderConfiguration : IEntityTypeConfiguration<ChatProvider>
{
    public void Configure(EntityTypeBuilder<ChatProvider> builder)
    {
        builder.ToTable("ChatProviders");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(p => p.Name)
            .IsUnique();

        builder.Property(p => p.BaseUrl)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(p => p.ApiKey)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(p => p.Enabled)
            .IsRequired();

        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.UpdatedAt);
    }
}

public sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("ChatConversations");

        builder.Property(c => c.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Title)
            .IsRequired()
            .HasMaxLength(240);

        builder.Property(c => c.Model)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.ProviderName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.CreatedAt);
        builder.Property(c => c.UpdatedAt);

        builder.Property(c => c.Version)
            .IsConcurrencyToken();

        builder.HasIndex(c => c.UpdatedAt);

        builder.HasMany(c => c.Messages)
            .WithOne()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");

        builder.Property(m => m.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatMessageId>());

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ConversationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.Property(m => m.Role)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion(new StringValueObjectConverter<ChatMessageRole>());

        builder.Property(m => m.Content)
            .IsRequired();

        builder.Property(m => m.ToolCallsJson);
        builder.Property(m => m.ToolCallId)
            .HasMaxLength(128);
        builder.Property(m => m.ToolName)
            .HasMaxLength(64);
        builder.Property(m => m.Refused)
            .IsRequired();
        builder.Property(m => m.ImagePath)
            .HasMaxLength(512);
        builder.Property(m => m.TokensIn);
        builder.Property(m => m.TokensOut);
        builder.Property(m => m.Model)
            .HasMaxLength(128);
        builder.Property(m => m.CreatedAt);

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}
