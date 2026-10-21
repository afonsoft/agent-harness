using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Agents;

namespace Taskboard.EntityFrameworkCore.Configurations;

/// <summary>SPEC-20261020-agents-feed-notifications RF-001 + NFR composite index.</summary>
public sealed class AgentFeedItemConfiguration : IEntityTypeConfiguration<AgentFeedItem>
{
    public void Configure(EntityTypeBuilder<AgentFeedItem> builder)
    {
        builder.ToTable("AgentFeedItems");

        builder.Property(i => i.Id).HasMaxLength(96);
        builder.HasKey(i => i.Id);

        builder.Property(i => i.UserId).IsRequired().HasMaxLength(128);
        builder.Property(i => i.Kind).IsRequired().HasConversion<string>().HasMaxLength(24);
        builder.Property(i => i.Title).IsRequired().HasMaxLength(AgentFeedItem.TitleMaxLength);
        builder.Property(i => i.Body).HasMaxLength(AgentFeedItem.BodyMaxLength);
        builder.Property(i => i.DeepLink).IsRequired().HasMaxLength(AgentFeedItem.DeepLinkMaxLength);
        builder.Property(i => i.SessionKey).HasMaxLength(128);
        builder.Property(i => i.Cli).HasMaxLength(32);

        // NFR: feed page query is (UserId, AtUtc desc) — composite index.
        builder.HasIndex(i => new { i.UserId, i.AtUtc });
        builder.HasIndex(i => new { i.UserId, i.SessionKey });
    }
}

/// <summary>SPEC RF-004: per-user read marker (key = userId|itemId).</summary>
public sealed class UserFeedReadConfiguration : IEntityTypeConfiguration<UserFeedRead>
{
    public void Configure(EntityTypeBuilder<UserFeedRead> builder)
    {
        builder.ToTable("UserFeedReads");

        builder.Property(r => r.Id).HasMaxLength(224);
        builder.HasKey(r => r.Id);

        builder.Property(r => r.UserId).IsRequired().HasMaxLength(128);
        builder.Property(r => r.ItemId).IsRequired().HasMaxLength(96);

        builder.HasIndex(r => r.UserId);
    }
}

/// <summary>SPEC RF-006: per-user per-category preference override.</summary>
public sealed class UserFeedPreferenceConfiguration : IEntityTypeConfiguration<UserFeedPreference>
{
    public void Configure(EntityTypeBuilder<UserFeedPreference> builder)
    {
        builder.ToTable("UserFeedPreferences");

        builder.Property(p => p.Id).HasMaxLength(160);
        builder.HasKey(p => p.Id);

        builder.Property(p => p.UserId).IsRequired().HasMaxLength(128);
        builder.Property(p => p.Kind).IsRequired().HasConversion<string>().HasMaxLength(24);

        builder.HasIndex(p => p.UserId);
    }
}
