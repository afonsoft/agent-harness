using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Agents;

namespace Taskboard.EntityFrameworkCore.Configurations;

/// <summary>SPEC-20261020-provider-quota-usage RF-001: quota windows.</summary>
public sealed class ProviderUsageWindowConfiguration
    : IEntityTypeConfiguration<ProviderUsageWindow>
{
    public void Configure(EntityTypeBuilder<ProviderUsageWindow> builder)
    {
        builder.ToTable("ProviderUsageWindows");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).IsRequired().HasMaxLength(32);
        builder.Property(x => x.WindowKind).IsRequired().HasMaxLength(16);
        builder.Property(x => x.Source).IsRequired().HasMaxLength(16);
        builder.Property(x => x.Confidence).IsRequired().HasMaxLength(16);
        builder.Property(x => x.RawJson).HasMaxLength(2048);
        // 10 doubles × ~8 chars — 128 is generous headroom.
        builder.Property(x => x.RecentPctsJson).HasMaxLength(128);

        // One row per (provider, window) — upsert target.
        builder.HasIndex(x => new { x.Provider, x.WindowKind }).IsUnique();
        builder.HasIndex(x => x.SampledAtUtc);
    }
}
