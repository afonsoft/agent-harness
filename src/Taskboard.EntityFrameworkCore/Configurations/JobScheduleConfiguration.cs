using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class JobScheduleConfiguration : IEntityTypeConfiguration<JobSchedule>
{
    public void Configure(EntityTypeBuilder<JobSchedule> builder)
    {
        builder.ToTable("JobSchedules");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.JobKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(x => x.JobKey)
            .IsUnique();

        builder.Property(x => x.Enabled)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();
    }
}
