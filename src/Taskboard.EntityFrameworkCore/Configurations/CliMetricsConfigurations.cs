using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.CliMetrics;
using Taskboard.Domain.Entities.CliMetrics;
using Taskboard.EntityFrameworkCore.ValueConverters;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class CliMetricSourceConfiguration : IEntityTypeConfiguration<CliMetricSource>
{
    public void Configure(EntityTypeBuilder<CliMetricSource> builder)
    {
        builder.ToTable("CliMetricSources");

        builder.Property(s => s.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<CliMetricSourceId>());
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Kind).IsRequired().HasMaxLength(32).HasConversion<string>();
        builder.Property(s => s.SourceName).IsRequired().HasMaxLength(128);
        builder.Property(s => s.RelativePath).IsRequired().HasMaxLength(512);
        builder.Property(s => s.ResolvedPath).HasMaxLength(1024);
        builder.Property(s => s.Status).IsRequired().HasMaxLength(32).HasConversion<string>();
        builder.Property(s => s.SchemaFingerprint).HasMaxLength(2048);
        builder.Property(s => s.WatermarkCursor).HasMaxLength(2048);
        builder.Property(s => s.LastError).HasMaxLength(2048);
        builder.Property(s => s.FileModifiedUtc);
        builder.Property(s => s.FileSizeBytes);
        builder.Property(s => s.LastSyncUtc);
        builder.Property(s => s.RowCount);
        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.UpdatedAt);

        builder.HasIndex(s => new { s.Kind, s.SourceName, s.ResolvedPath }).IsUnique();
        builder.Property(s => s.Version).IsConcurrencyToken();
    }
}

public sealed class CliSessionMetricConfiguration : IEntityTypeConfiguration<CliSessionMetric>
{
    public void Configure(EntityTypeBuilder<CliSessionMetric> builder)
    {
        builder.ToTable("CliSessionMetrics");

        builder.Property(s => s.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<CliSessionMetricId>());
        builder.HasKey(s => s.Id);

        builder.Property(s => s.SourceId)
            .IsRequired()
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<CliMetricSourceId>());
        builder.Property(s => s.Kind).IsRequired().HasMaxLength(32).HasConversion<string>();
        builder.Property(s => s.ExternalId).IsRequired().HasMaxLength(256);
        builder.Property(s => s.Title).HasMaxLength(512);
        builder.Property(s => s.ModelName).HasMaxLength(128);
        builder.Property(s => s.StartedAtUtc);
        builder.Property(s => s.EndedAtUtc);
        builder.Property(s => s.MessageCount);
        builder.Property(s => s.TokensInput);
        builder.Property(s => s.TokensOutput);
        builder.Property(s => s.TokensCached);
        builder.Property(s => s.CostUsd).HasPrecision(18, 6);
        builder.Property(s => s.IngestedAtUtc);

        // Dedupe key — re-ingested rows update, never duplicate (RF-003).
        builder.HasIndex(s => new { s.SourceId, s.ExternalId }).IsUnique();
        builder.HasIndex(s => s.StartedAtUtc);
        builder.HasIndex(s => s.Kind);
        builder.Property(s => s.Version).IsConcurrencyToken();
    }
}

public sealed class CliDailyUsageAggregateConfiguration : IEntityTypeConfiguration<CliDailyUsageAggregate>
{
    public void Configure(EntityTypeBuilder<CliDailyUsageAggregate> builder)
    {
        builder.ToTable("CliDailyUsageAggregates");

        builder.Property(a => a.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<CliDailyUsageAggregateId>());
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Kind).IsRequired().HasMaxLength(32).HasConversion<string>();
        builder.Property(a => a.Day).IsRequired().HasMaxLength(10);
        builder.Property(a => a.SessionsCount);
        builder.Property(a => a.MessagesCount);
        builder.Property(a => a.TokensInput);
        builder.Property(a => a.TokensOutput);
        builder.Property(a => a.TokensCached);
        builder.Property(a => a.CostUsd).HasPrecision(18, 6);
        builder.Property(a => a.ModelsJson);
        builder.Property(a => a.UpdatedAt);

        builder.HasIndex(a => new { a.Kind, a.Day }).IsUnique();
        builder.Property(a => a.Version).IsConcurrencyToken();
    }
}
