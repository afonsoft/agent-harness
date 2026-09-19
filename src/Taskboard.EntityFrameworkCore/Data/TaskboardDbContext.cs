using Microsoft.EntityFrameworkCore;
using Taskboard.Domain.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Domain.Entities.CliMetrics;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Domain.Issues;

namespace Taskboard.EntityFrameworkCore.Data;

public sealed class TaskboardDbContext : DbContext
{
    public TaskboardDbContext(DbContextOptions<TaskboardDbContext> options)
        : base(options)
    {
    }

    public DbSet<AiChatThread> AiChatThreads => Set<AiChatThread>();
    public DbSet<AiChatRun> AiChatRuns => Set<AiChatRun>();
    public DbSet<AiChatEvent> AiChatEvents => Set<AiChatEvent>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<AgentPreference> AgentPreferences => Set<AgentPreference>();
    public DbSet<AgentLog> AgentLogs => Set<AgentLog>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<IssueHistoryEvent> IssueHistoryEvents => Set<IssueHistoryEvent>();
    public DbSet<ConfigurationOverride> ConfigurationOverrides => Set<ConfigurationOverride>();
    public DbSet<WorktreeSession> WorktreeSessions => Set<WorktreeSession>();
    public DbSet<ProjectMemoryItem> ProjectMemoryItems => Set<ProjectMemoryItem>();
    public DbSet<VerificationReport> VerificationReports => Set<VerificationReport>();
    public DbSet<CliMetricSource> CliMetricSources => Set<CliMetricSource>();
    public DbSet<CliSessionMetric> CliSessionMetrics => Set<CliSessionMetric>();
    public DbSet<CliDailyUsageAggregate> CliDailyUsageAggregates => Set<CliDailyUsageAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskboardDbContext).Assembly);
    }
}
