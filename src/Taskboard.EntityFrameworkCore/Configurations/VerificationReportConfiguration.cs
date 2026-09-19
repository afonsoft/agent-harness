using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities.Harness;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Taskboard.Harness;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class VerificationReportConfiguration : IEntityTypeConfiguration<VerificationReport>
{
    public void Configure(EntityTypeBuilder<VerificationReport> builder)
    {
        builder.ToTable("VerificationReports");

        builder.Property(r => r.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<VerificationReportId>());

        builder.HasKey(r => r.Id);

        builder.Property(r => r.WorktreePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(32)
            .HasConversion<string>();

        builder.Property(r => r.IsSuccess);
        builder.Property(r => r.CoveragePercent);
        builder.Property(r => r.Attempts);

        builder.Property(r => r.DetailsJson)
            .IsRequired();

        builder.Property(r => r.CreatedAt);

        builder.HasIndex(r => r.CreatedAt);

        builder.Property(r => r.Version)
            .IsConcurrencyToken();
    }
}
