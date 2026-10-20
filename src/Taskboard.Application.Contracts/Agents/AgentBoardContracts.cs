using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// SPEC-20261020-agent-board-v2 RF-001: one card on the agents kanban —
/// a live task, run, mailbox thread or scope-level attention surface.
/// </summary>
public sealed record AgentBoardCard(
    string Id,
    /// <summary><c>task</c> | <c>run</c> | <c>mailbox</c> | <c>scope</c> | <c>cli</c></summary>
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
    /// <summary>PR link extracted from the mailbox when a leg was promoted.</summary>
    string? PrUrl,
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

/// <summary>
/// SPEC-20261020-agent-board-v2 RF-004: board toolbar filters — all optional,
/// applied in-memory after projection (deep-linkable via query-string).
/// </summary>
public sealed record AgentBoardQuery(
    /// <summary>Substring match on title/preview/cli/repo/worktree.</summary>
    string? Text = null,
    /// <summary>Exact cli name (case-insensitive).</summary>
    string? Cli = null,
    /// <summary>Exact attention state name (case-insensitive).</summary>
    string? State = null,
    /// <summary>Exact repo name (case-insensitive).</summary>
    string? Repo = null,
    /// <summary>Only cards with a linked PR.</summary>
    bool? HasPr = null);

/// <summary>SPEC RF-001/RF-006: board projection + unread markers.</summary>
public interface IAgentBoardService
{
    Task<AgentBoardSnapshot> GetSnapshotAsync(
        string scope, string userId, int take = 20,
        AgentBoardQuery? query = null, CancellationToken ct = default);

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
