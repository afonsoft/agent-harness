using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Issues;

namespace Taskboard.EntityFrameworkCore.Issues;

public sealed class IssueHistoryEventConfiguration : IEntityTypeConfiguration<IssueHistoryEvent>
{
    public void Configure(EntityTypeBuilder<IssueHistoryEvent> builder)
    {
        builder.ToTable("IssueHistoryEvents");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.IssueId).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Repository).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Kind).IsRequired();
        builder.Property(x => x.From).HasMaxLength(255);
        builder.Property(x => x.To).HasMaxLength(255);
        builder.Property(x => x.Detail).HasMaxLength(512);
        builder.Property(x => x.OccurredAt)
            .IsRequired()
            .HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));

        builder.HasIndex(x => x.IssueId);
    }
}
