namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261011-chat-workspace-panel RF-003: the effective workspace a
/// conversation's workspace panel operates on — the latest run's worktree
/// when it has one, else the conversation workspace path.
/// </summary>
public sealed record ConversationWorkspaceDto(
    string? Path,
    string? Repository,
    bool IsGit,
    string? Branch,
    bool Dirty,
    string? WorktreeRunId);

/// <summary>Lightweight git status of a workspace path (non-repo → IsGit=false).</summary>
public sealed record ChatWorkspaceStatusDto(bool IsGit, string? Branch, bool Dirty);
