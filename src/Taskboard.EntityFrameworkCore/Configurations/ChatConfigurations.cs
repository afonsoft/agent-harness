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
        builder.Property(c => c.ArchivedAt);

        builder.Property(c => c.Version)
            .IsConcurrencyToken();

        builder.HasIndex(c => c.UpdatedAt);
        builder.HasIndex(c => c.ArchivedAt);

        builder.HasMany(c => c.Messages)
            .WithOne()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// SPEC-20261005-chat-background-resume RF-001: durable run rows — the detached
/// executor's lifecycle + in-flight checkpoints. Cascade-deleted with the
/// conversation.
/// </summary>
public sealed class ChatRunConfiguration : IEntityTypeConfiguration<ChatRun>
{
    public void Configure(EntityTypeBuilder<ChatRun> builder)
    {
        builder.ToTable("ChatRuns");

        builder.Property(r => r.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatRunId>());

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ConversationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.Property(r => r.TriggerMessageId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatMessageId>());

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion(new StringValueObjectConverter<ChatRunStatus>());

        builder.Property(r => r.PartialContent);
        builder.Property(r => r.PartialReasoning);
        builder.Property(r => r.Error)
            .HasMaxLength(1024);
        builder.Property(r => r.TokensIn);
        builder.Property(r => r.TokensOut);
        builder.Property(r => r.CreatedAt);
        builder.Property(r => r.StartedAt);
        builder.Property(r => r.FinishedAt);

        builder.Property(r => r.Version)
            .IsConcurrencyToken();

        // One live run lookup per conversation (attach path + history badge)
        // and the dispatcher's queued-FIFO scan.
        builder.HasIndex(r => new { r.ConversationId, r.Status });
        builder.HasIndex(r => r.CreatedAt);

        builder.HasOne<ChatConversation>()
            .WithMany()
            .HasForeignKey(r => r.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// SPEC-20261005-chat-background-resume RF-009: browser Web Push endpoints —
/// unique per endpoint; re-subscribe rotates keys in place.
/// </summary>
public sealed class ChatPushSubscriptionConfiguration : IEntityTypeConfiguration<ChatPushSubscription>
{
    public void Configure(EntityTypeBuilder<ChatPushSubscription> builder)
    {
        builder.ToTable("ChatPushSubscriptions");

        builder.Property(s => s.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatPushSubscriptionId>());

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Endpoint)
            .IsRequired()
            .HasMaxLength(2048);

        builder.HasIndex(s => s.Endpoint)
            .IsUnique();

        builder.Property(s => s.P256dh)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.Auth)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.UserAgent)
            .HasMaxLength(512);

        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.UpdatedAt);

        builder.Property(s => s.Version)
            .IsConcurrencyToken();
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
