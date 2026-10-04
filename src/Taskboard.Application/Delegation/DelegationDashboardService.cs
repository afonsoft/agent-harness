using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;

namespace Taskboard.Application.Delegation;

/// <summary>
/// SPEC-20261006 RF-001: aggregates delegation tasks, live agent runs, unread
/// mailbox escalations/decisions and idle CLIs into the four dashboard columns.
/// </summary>
public sealed class DelegationDashboardService : IDelegationDashboardService
{
    private readonly IDelegationTaskRepository _tasks;
    private readonly IAgentMailboxRepository _mailbox;
    private readonly IAgentOrchestrationService _orchestration;
    private readonly IAgentDiscoveryService _discovery;

    public DelegationDashboardService(
        IDelegationTaskRepository tasks,
        IAgentMailboxRepository mailbox,
        IAgentOrchestrationService orchestration,
        IAgentDiscoveryService discovery)
    {
        _tasks = tasks;
        _mailbox = mailbox;
        _orchestration = orchestration;
        _discovery = discovery;
    }

    public async Task<AgentDashboardDto> GetAsync(
        string scope, int take = 20, CancellationToken ct = default)
    {
        var tasks = await _tasks.ListByScopeAsync(scope, take: 200, ct).ConfigureAwait(false);
        var unread = await _mailbox.ListForRecipientAsync(
            scope, ["@all", "@idle"], take, unreadOnly: true, ct).ConfigureAwait(false);
        var runs = await _orchestration.GetLatestRunsAsync(ct).ConfigureAwait(false);
        var discovered = await _discovery.DiscoverAsync(ct).ConfigureAwait(false);

        var needsYou = new List<DashboardItemDto>();
        var working = new List<DashboardItemDto>();
        var done = new List<DashboardItemDto>();
        var busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in unread.Where(m =>
            m.Kind is AgentMailboxKinds.Escalation or AgentMailboxKinds.Decision))
        {
            needsYou.Add(new DashboardItemDto(
                "mailbox", message.Id, message.Kind,
                message.Payload, message.FromAgent, message.CreatedAt));
        }

        foreach (var task in tasks)
        {
            busy.Add(task.CliName);
            switch (task.Status)
            {
                case DelegationTaskStatus.Running:
                    working.Add(new DashboardItemDto(
                        "task", task.Id, task.Prompt, StatusDetail(task),
                        task.CliName, task.LastHeartbeatAt ?? task.StartedAt ?? task.CreatedAt,
                        task.FanoutGroupId));
                    break;
                case DelegationTaskStatus.Ready:
                case DelegationTaskStatus.Pending:
                    working.Add(new DashboardItemDto(
                        "task", task.Id, task.Prompt, task.Status.ToString().ToLowerInvariant(),
                        task.CliName, task.CreatedAt, task.FanoutGroupId));
                    break;
                case DelegationTaskStatus.Failed:
                case DelegationTaskStatus.Stale:
                case DelegationTaskStatus.Cancelled:
                    needsYou.Add(new DashboardItemDto(
                        "task", task.Id, task.Prompt,
                        $"{task.Status.ToString().ToLowerInvariant()}: {task.Error}",
                        task.CliName, task.FinishedAt ?? task.CreatedAt, task.FanoutGroupId));
                    break;
                case DelegationTaskStatus.Done:
                    done.Add(new DashboardItemDto(
                        "task", task.Id, task.Prompt, task.ResultSummary ?? string.Empty,
                        task.CliName, task.FinishedAt ?? task.CreatedAt, task.FanoutGroupId));
                    break;
            }
        }

        foreach (var run in runs.Where(r =>
            r.State is AgentRunState.Queued or AgentRunState.Running))
        {
            working.Add(new DashboardItemDto(
                "run", run.Id.ToString("N"), run.IssueId,
                run.State.ToString().ToLowerInvariant(),
                run.AgentType.ToString().ToLowerInvariant(), run.StartedAt.UtcDateTime));
            busy.Add(run.AgentType.ToString());
        }

        var idle = discovered
            .Where(a => a.Status == AgentStatus.Available && !busy.Contains(a.Type.ToString()))
            .Select(a => new DashboardItemDto(
                "cli", a.Type.ToString(), a.Name, "installed — no active work",
                a.Type.ToString(), null))
            .ToList();

        return new AgentDashboardDto(
            Trim(needsYou, take), Trim(working, take), Trim(done, take), idle);
    }

    private static string StatusDetail(DelegationTaskDto task) =>
        $"running since {task.StartedAt:HH:mm:ss}";

    private static IReadOnlyList<DashboardItemDto> Trim(List<DashboardItemDto> items, int take) =>
        items.OrderByDescending(i => i.Timestamp ?? DateTime.MinValue).Take(take).ToList();
}
