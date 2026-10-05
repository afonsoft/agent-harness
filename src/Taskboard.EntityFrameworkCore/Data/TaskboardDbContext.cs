using Microsoft.EntityFrameworkCore;
using Taskboard.Domain.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Domain.Entities.CliMetrics;
using Taskboard.Domain.Entities.Delegation;
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
    public DbSet<AgentRunEvent> AgentRunEvents => Set<AgentRunEvent>();
    public DbSet<IssueHistoryEvent> IssueHistoryEvents => Set<IssueHistoryEvent>();
    public DbSet<ConfigurationOverride> ConfigurationOverrides => Set<ConfigurationOverride>();
    public DbSet<AgentCliDefinition> AgentCliDefinitions => Set<AgentCliDefinition>();
    public DbSet<WorktreeSession> WorktreeSessions => Set<WorktreeSession>();
    public DbSet<ProjectMemoryItem> ProjectMemoryItems => Set<ProjectMemoryItem>();
    public DbSet<VerificationReport> VerificationReports => Set<VerificationReport>();
    public DbSet<CliMetricSource> CliMetricSources => Set<CliMetricSource>();
    public DbSet<CliSessionMetric> CliSessionMetrics => Set<CliSessionMetric>();
    public DbSet<CliDailyUsageAggregate> CliDailyUsageAggregates => Set<CliDailyUsageAggregate>();
    public DbSet<PipelineExecution> PipelineExecutions => Set<PipelineExecution>();
    public DbSet<PipelineStageExecution> PipelineStageExecutions => Set<PipelineStageExecution>();
    public DbSet<ModelPriceRate> ModelPriceRates => Set<ModelPriceRate>();
    public DbSet<RunCostMetric> RunCostMetrics => Set<RunCostMetric>();
    public DbSet<JobSchedule> JobSchedules => Set<JobSchedule>();
    public DbSet<ChatProvider> ChatProviders => Set<ChatProvider>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatRun> ChatRuns => Set<ChatRun>();
    public DbSet<ChatApproval> ChatApprovals => Set<ChatApproval>();
    public DbSet<ChatSteer> ChatSteers => Set<ChatSteer>();
    public DbSet<ChatAttachment> ChatAttachments => Set<ChatAttachment>();
    public DbSet<ChatMessageFeedback> ChatMessageFeedbacks => Set<ChatMessageFeedback>();
    public DbSet<ChatRunDeliverable> ChatRunDeliverables => Set<ChatRunDeliverable>();
    public DbSet<ChatPushSubscription> ChatPushSubscriptions => Set<ChatPushSubscription>();
    public DbSet<DelegationTask> DelegationTasks => Set<DelegationTask>();
    public DbSet<AgentMailboxMessage> AgentMailboxMessages => Set<AgentMailboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskboardDbContext).Assembly);
    }
}
