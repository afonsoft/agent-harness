using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261020-agent-board-v2 RF-001..RF-011: projects delegation tasks,
/// live runs, mailbox messages and attention states into kanban cards.
/// Never reads CLI transcripts — previews come from Harness logs/mailbox
/// only (CliDatabaseMap denied-tables rule).
/// </summary>
public sealed class AgentBoardService : IAgentBoardService
{
    private const int PreviewMax = 160;
    private const int TitleMax = 80;

    private readonly IDelegationTaskRepository _tasks;
    private readonly IAgentMailboxRepository _mailbox;
    private readonly IAgentOrchestrationService _orchestration;
    private readonly IAgentDiscoveryService _discovery;
    private readonly IAgentSessionStateRepository _states;
    private readonly IAgentBoardReadMarkerRepository _markers;
    private readonly TimeProvider _time;

    public AgentBoardService(
        IDelegationTaskRepository tasks,
        IAgentMailboxRepository mailbox,
        IAgentOrchestrationService orchestration,
        IAgentDiscoveryService discovery,
        IAgentSessionStateRepository states,
        IAgentBoardReadMarkerRepository markers,
        TimeProvider? timeProvider = null)
    {
        _tasks = tasks;
        _mailbox = mailbox;
        _orchestration = orchestration;
        _discovery = discovery;
        _states = states;
        _markers = markers;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<AgentBoardSnapshot> GetSnapshotAsync(
        string scope, string userId, int take = 20, CancellationToken ct = default)
    {
        var tasks = await _tasks.ListByScopeAsync(scope, take: 200, ct).ConfigureAwait(false);
        var mailbox = await _mailbox.ListByScopeAsync(scope, take: 100, ct).ConfigureAwait(false);
        var runs = await _orchestration.GetLatestRunsAsync(ct).ConfigureAwait(false);
        var discovered = await _discovery.DiscoverAsync(ct).ConfigureAwait(false);
        var states = await _states.ListAsync(ct: ct).ConfigureAwait(false);
        var markers = await _markers.ListAsync(userId, ct).ConfigureAwait(false);
        var now = _time.GetUtcNow().UtcDateTime;

        var statesByKey = states.ToDictionary(s => s.SessionKey, StringComparer.Ordinal);
        var lastMailboxByScope = mailbox
            .GroupBy(m => m.Scope, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.CreatedAt).First(),
                StringComparer.Ordinal);

        var needsYou = new List<AgentBoardCard>();
        var working = new List<AgentBoardCard>();
        var done = new List<AgentBoardCard>();
        var busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Scope-level attention (decision/escalation posted to the mailbox).
        if (statesByKey.TryGetValue($"scope:{scope}", out var scopeState)
            && scopeState.State is AgentAttentionState.WaitingYou or AgentAttentionState.Blocked)
        {
            needsYou.Add(new AgentBoardCard(
                $"scope:{scope}", "scope", scopeState.Cli,
                Title($"decision needed — {scope}"),
                scopeState.State,
                Preview(scopeState.Evidence ?? lastMailboxByScope.GetValueOrDefault(scope)?.Payload),
                scopeState.LastSignalAtUtc, null, null, null, null,
                Unread(markers, $"scope:{scope}", scopeState.LastSignalAtUtc),
                "/agents?tab=dashboard"));
        }

        foreach (var task in tasks)
        {
            busy.Add(task.CliName);
            var key = $"task:{task.Id}";
            var attention = statesByKey.GetValueOrDefault(key);
            var state = attention?.State ?? MapTaskState(task.Status);
            var card = new AgentBoardCard(
                key, "task", task.CliName, Title(task.Prompt), state,
                Preview(attention?.Evidence ?? task.ResultSummary ?? task.Error ?? task.Prompt),
                attention?.LastSignalAtUtc ?? task.FinishedAt ?? task.LastHeartbeatAt ?? task.CreatedAt,
                task.StartedAt,
                RepoName(task.RepositoryPath), task.WorktreeRunId, task.FanoutGroupId,
                Unread(markers, key,
                    attention?.LastSignalAtUtc ?? task.FinishedAt ?? task.LastHeartbeatAt),
                $"/agents?tab=dashboard&focus={task.Id}");

            switch (state)
            {
                case AgentAttentionState.WaitingYou:
                case AgentAttentionState.Blocked:
                    needsYou.Add(card);
                    break;
                case AgentAttentionState.Done:
                case AgentAttentionState.Exited:
                    done.Add(card);
                    break;
                case AgentAttentionState.Idle:
                case AgentAttentionState.Unverifiable:
                    working.Add(card); // still an open run — surface, tinted later
                    break;
                default:
                    working.Add(card);
                    break;
            }
        }

        foreach (var run in runs.Where(r =>
            r.State is AgentRunState.Queued or AgentRunState.Running))
        {
            var key = $"run:{run.Id:N}";
            var attention = statesByKey.GetValueOrDefault(key);
            var state = attention?.State ?? AgentAttentionState.Working;
            busy.Add(run.AgentType.ToString());
            var card = new AgentBoardCard(
                key, "run", run.AgentType.ToString().ToLowerInvariant(),
                Title($"run {run.IssueId}"), state,
                Preview(attention?.Evidence ?? run.State.ToString()),
                attention?.LastSignalAtUtc ?? run.StartedAt.UtcDateTime,
                run.StartedAt.UtcDateTime, null, null, null,
                Unread(markers, key, attention?.LastSignalAtUtc ?? run.StartedAt.UtcDateTime),
                $"/cockpit?issue={Uri.EscapeDataString(run.IssueId)}");

            if (state is AgentAttentionState.WaitingYou or AgentAttentionState.Blocked)
            {
                needsYou.Add(card);
            }
            else
            {
                working.Add(card);
            }
        }

        var idle = discovered
            .Where(a => a.Status == AgentStatus.Available && !busy.Contains(a.Type.ToString()))
            .Select(a => new AgentBoardCard(
                $"cli:{a.Type}", "cli", a.Type.ToString().ToLowerInvariant(), a.Name,
                AgentAttentionState.Idle, "installed — no active work",
                null, null, null, null, null, false, "/agents?tab=cli"))
            .ToList();

        var unreadTotal = needsYou.Concat(working).Concat(done).Count(c => c.Unread);
        return new AgentBoardSnapshot(
            Sort(needsYou, take, needsYouOrder: true),
            Sort(working, take, needsYouOrder: false),
            Sort(done, take, needsYouOrder: true),
            idle,
            unreadTotal, now);
    }

    public Task MarkReadAsync(string userId, string cardId, CancellationToken ct = default) =>
        _markers.MarkReadAsync(userId, cardId, _time.GetUtcNow().UtcDateTime, ct);

    private static AgentAttentionState MapTaskState(DelegationTaskStatus status) =>
        status switch
        {
            DelegationTaskStatus.Running => AgentAttentionState.Working,
            DelegationTaskStatus.Ready => AgentAttentionState.Working,
            DelegationTaskStatus.Pending => AgentAttentionState.Idle,
            DelegationTaskStatus.Done => AgentAttentionState.Done,
            DelegationTaskStatus.Failed => AgentAttentionState.Blocked,
            DelegationTaskStatus.Stale => AgentAttentionState.Blocked,
            DelegationTaskStatus.Cancelled => AgentAttentionState.Exited,
            _ => AgentAttentionState.Working,
        };

    private static string Title(string? text)
    {
        var first = (text ?? string.Empty).Trim().Split('\n')[0].Trim();
        return string.IsNullOrEmpty(first)
            ? "session"
            : first.Length <= TitleMax ? first : first[..TitleMax] + "…";
    }

    private static string Preview(string? text)
    {
        var collapsed = string.Join(' ',
            (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= PreviewMax ? collapsed : collapsed[..PreviewMax] + "…";
    }

    private static string? RepoName(string? repositoryPath) =>
        string.IsNullOrWhiteSpace(repositoryPath)
            ? null
            : repositoryPath.TrimEnd('/').Split('/').LastOrDefault();

    private static bool Unread(
        IReadOnlyDictionary<string, DateTime> markers, string cardId, DateTime? activityAtUtc) =>
        activityAtUtc is { } at && (!markers.TryGetValue(cardId, out var read) || at > read);

    private static IReadOnlyList<AgentBoardCard> Sort(
        List<AgentBoardCard> cards, int take, bool needsYouOrder) =>
        (needsYouOrder
            ? cards.OrderByDescending(c => c.PreviewAtUtc ?? DateTime.MinValue)
            : cards.OrderBy(c => c.StartedAtUtc ?? DateTime.MaxValue))
        .Take(take).ToList();
}
