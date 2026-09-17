using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Agents;

namespace Taskboard.EntityFrameworkCore.Agents;

public sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("AgentRuns");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.IssueId).IsRequired().HasMaxLength(255);
        builder.Property(x => x.AgentType).IsRequired();
        builder.Property(x => x.State).IsRequired();
        builder.Property(x => x.StartedAt)
            .IsRequired()
            .HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        builder.Property(x => x.FinishedAt)
            .HasConversion(
                v => v.HasValue ? v.Value.ToUnixTimeMilliseconds() : (long?)null,
                v => v.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : (DateTimeOffset?)null);

        builder.HasIndex(x => x.IssueId);
    }
}
