using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities.Automation;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Taskboard.ValueObjects;

namespace Taskboard.EntityFrameworkCore.Configurations;

/// <summary>SPEC-20261021-agent-automations RF-001 — due-scan index on enabled rows.</summary>
public sealed class AgentAutomationConfiguration : IEntityTypeConfiguration<AgentAutomation>
{
    public void Configure(EntityTypeBuilder<AgentAutomation> builder)
    {
        builder.ToTable("AgentAutomations");

        builder.Property(a => a.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<AgentAutomationId>());
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(AgentAutomation.MaxNameLength).IsRequired();
        builder.Property(a => a.Prompt).HasMaxLength(AgentAutomation.MaxPromptLength).IsRequired();
        builder.Property(a => a.AgentCli).HasMaxLength(32).IsRequired();
        builder.Property(a => a.Target).HasMaxLength(AgentAutomation.MaxTargetLength).IsRequired();
        builder.Property(a => a.ScheduleKind).HasMaxLength(16).IsRequired();
        builder.Property(a => a.CronExpression).HasMaxLength(AgentAutomation.MaxCronLength);
        builder.Property(a => a.TimeZoneId).HasMaxLength(AgentAutomation.MaxTimeZoneLength);
        builder.Property(a => a.NextFireAtUtc);
        builder.Property(a => a.Enabled);
        builder.Property(a => a.MissedRunGraceMinutes);
        builder.Property(a => a.ReuseSession);
        builder.Property(a => a.ExtraAgentArgs).HasMaxLength(AgentAutomation.MaxExtraArgsLength);
        builder.Property(a => a.Precheck).HasMaxLength(AgentAutomation.MaxPrecheckLength);
        builder.Property(a => a.Host).HasMaxLength(128);
        builder.Property(a => a.CreatedBy).HasMaxLength(128);
        builder.Property(a => a.CreatedAt);
        builder.Property(a => a.LastFiredAtUtc);
        builder.Property(a => a.Version).IsConcurrencyToken();

        // Due scan: enabled rows ordered by next fire + per-user listing.
        builder.HasIndex(a => new { a.Enabled, a.NextFireAtUtc });
        builder.HasIndex(a => a.CreatedBy);
    }
}

/// <summary>SPEC-20261021-agent-automations RF-002 — run history per automation.</summary>
public sealed class AgentAutomationRunConfiguration : IEntityTypeConfiguration<AgentAutomationRun>
{
    public void Configure(EntityTypeBuilder<AgentAutomationRun> builder)
    {
        builder.ToTable("AgentAutomationRuns");

        builder.Property(r => r.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<AgentAutomationRunId>());
        builder.HasKey(r => r.Id);

        builder.Property(r => r.AutomationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<AgentAutomationId>());

        builder.Property(r => r.FiredAtUtc);
        builder.Property(r => r.Outcome).HasMaxLength(16).IsRequired();
        builder.Property(r => r.ExitCode);
        builder.Property(r => r.TaskId).HasMaxLength(128);
        builder.Property(r => r.SessionId).HasMaxLength(128);
        builder.Property(r => r.SkipReason).HasMaxLength(AgentAutomationRun.MaxSkipReasonLength);
        builder.Property(r => r.DurationMs);
        builder.Property(r => r.FinishedAtUtc);

        builder.HasIndex(r => new { r.AutomationId, r.FiredAtUtc });
        builder.HasIndex(r => r.Outcome);

        builder.HasOne<AgentAutomation>()
            .WithMany()
            .HasForeignKey(r => r.AutomationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
