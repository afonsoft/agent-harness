using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities.Harness;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class ModelPriceRateConfiguration : IEntityTypeConfiguration<ModelPriceRate>
{
    public void Configure(EntityTypeBuilder<ModelPriceRate> builder)
    {
        builder.ToTable("ModelPriceRates");

        builder.Property(r => r.Provider).IsRequired().HasMaxLength(64);
        builder.Property(r => r.ModelPattern).IsRequired().HasMaxLength(128);
        builder.Property(r => r.InputPer1M).HasPrecision(18, 6);
        builder.Property(r => r.OutputPer1M).HasPrecision(18, 6);
        builder.Property(r => r.CacheWritePer1M).HasPrecision(18, 6);
        builder.Property(r => r.CacheReadPer1M).HasPrecision(18, 6);

        builder.HasIndex(r => r.ModelPattern).IsUnique();

        // Market-default price list (USD per 1M tokens). Editable data — the
        // table is the source of truth and rows can be updated in place.
        builder.HasData(
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000001"), "anthropic", "claude-3-7-sonnet", 3.00m, 15.00m, 3.75m, 0.30m),
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000002"), "anthropic", "claude-sonnet-4", 3.00m, 15.00m, 3.75m, 0.30m),
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000003"), "anthropic", "claude-haiku-4", 1.00m, 5.00m, 1.25m, 0.10m),
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000004"), "anthropic", "claude-opus-4", 15.00m, 75.00m, 18.75m, 1.50m),
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000005"), "openai", "gpt-5", 1.25m, 10.00m, 1.25m, 0.125m),
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000006"), "openai", "gpt-5-mini", 0.25m, 2.00m, 0.25m, 0.025m),
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000007"), "openai", "gpt-4.1", 2.00m, 8.00m, 2.00m, 0.50m),
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000008"), "deepseek", "deepseek-chat", 0.27m, 1.10m, 0.27m, 0.07m),
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000009"), "deepseek", "deepseek-reasoner", 0.55m, 2.19m, 0.55m, 0.14m),
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000010"), "google", "gemini-2.5-pro", 1.25m, 10.00m, 1.25m, 0.125m),
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000011"), "google", "gemini-2.5-flash", 0.30m, 2.50m, 0.30m, 0.03m),
            new ModelPriceRate(new Guid("a1000000-0000-0000-0000-000000000012"), "*", "*", 3.00m, 15.00m, 3.75m, 0.30m));
    }
}

public sealed class RunCostMetricConfiguration : IEntityTypeConfiguration<RunCostMetric>
{
    public void Configure(EntityTypeBuilder<RunCostMetric> builder)
    {
        builder.ToTable("RunCostMetrics");

        builder.Property(m => m.RunId).IsRequired().HasMaxLength(64);
        builder.Property(m => m.StageKey).HasMaxLength(128);
        builder.Property(m => m.AgentType).IsRequired().HasMaxLength(32).HasConversion<string>();
        builder.Property(m => m.ModelName).HasMaxLength(128);
        builder.Property(m => m.CostUsd).HasPrecision(18, 6);
        builder.Property(m => m.BudgetCapUsd).HasPrecision(18, 6);
        builder.Property(m => m.RecordedAtUtc);

        builder.HasIndex(m => m.RunId);
        builder.HasIndex(m => m.RecordedAtUtc);
        builder.HasIndex(m => new { m.AgentType, m.ModelName });
    }
}
