using Microsoft.EntityFrameworkCore;
using Taskboard.Domain.Agents;
using Taskboard.Domain.Entities;

namespace Taskboard.EntityFrameworkCore.Data;

public sealed class TaskboardDbContext : DbContext
{
    public TaskboardDbContext(DbContextOptions<TaskboardDbContext> options)
        : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Taskboard.Domain.Entities.Task> Tasks => Set<Taskboard.Domain.Entities.Task>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<TaskActivity> TaskActivities => Set<TaskActivity>();
    public DbSet<TaskRelation> TaskRelations => Set<TaskRelation>();
    public DbSet<WorkflowWorkspace> WorkflowWorkspaces => Set<WorkflowWorkspace>();
    public DbSet<WorkflowNode> WorkflowNodes => Set<WorkflowNode>();
    public DbSet<WorkflowSequence> WorkflowSequences => Set<WorkflowSequence>();
    public DbSet<ProjectSummary> ProjectSummaries => Set<ProjectSummary>();
    public DbSet<AiChatThread> AiChatThreads => Set<AiChatThread>();
    public DbSet<AiChatRun> AiChatRuns => Set<AiChatRun>();
    public DbSet<AiChatEvent> AiChatEvents => Set<AiChatEvent>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<AgentPreference> AgentPreferences => Set<AgentPreference>();
    public DbSet<AgentLog> AgentLogs => Set<AgentLog>();
    public DbSet<ConfigurationOverride> ConfigurationOverrides => Set<ConfigurationOverride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskboardDbContext).Assembly);
    }
}
