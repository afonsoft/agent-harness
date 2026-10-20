using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261020-agent-board-v2 RF-001: one card on the agents kanban —
/// a live task, run, mailbox thread or scope-level attention surface.
/// </summary>
public sealed record AgentBoardCard(
    string Id,
    /// <summary><c>task</c> | <c>run</c> | <c>mailbox</c> | <c>scope</c></summary>
    string Kind,
    string CliName,
    string SessionTitle,
    AgentAttentionState State,
    string PreviewText,
    DateTime? PreviewAtUtc,
    DateTime? StartedAtUtc,
    string? RepoName,
    string? WorktreeName,
    string? FanoutGroupId,
    bool Unread,
    string DeepLink);

/// <summary>The four kanban columns + generation stamp.</summary>
public sealed record AgentBoardSnapshot(
    IReadOnlyList<AgentBoardCard> NeedsYou,
    IReadOnlyList<AgentBoardCard> Working,
    IReadOnlyList<AgentBoardCard> Done,
    IReadOnlyList<AgentBoardCard> Idle,
    int UnreadTotal,
    DateTime GeneratedAtUtc);

/// <summary>SPEC RF-001/RF-006: board projection + unread markers.</summary>
public interface IAgentBoardService
{
    Task<AgentBoardSnapshot> GetSnapshotAsync(
        string scope, string userId, int take = 20, CancellationToken ct = default);

    /// <summary>Marks a card read for the user — idempotent.</summary>
    Task MarkReadAsync(string userId, string cardId, CancellationToken ct = default);
}

/// <summary>Persistence of per-user read markers.</summary>
public interface IAgentBoardReadMarkerRepository
{
    /// <summary>Read stamps for the user — key = card id.</summary>
    Task<IReadOnlyDictionary<string, DateTime>> ListAsync(
        string userId, CancellationToken ct = default);

    Task MarkReadAsync(string userId, string cardId, DateTime atUtc, CancellationToken ct = default);
}
