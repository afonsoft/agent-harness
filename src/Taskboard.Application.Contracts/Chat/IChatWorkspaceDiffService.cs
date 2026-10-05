namespace Taskboard.Application.Contracts.Chat;

/// <summary>Opaque run-start workspace state used by <see cref="IChatWorkspaceDiffService"/>.</summary>
public sealed record ChatWorkspaceSnapshot(string Porcelain, string Numstat);

/// <summary>
/// Git-aware workspace diff for the deliverables card
/// (SPEC-20261005-chat-attachments-feedback RF-007): snapshot at run start,
/// diff at run end. Returns null snapshots outside a git work tree — the
/// executor then relies on tracked file-tool edits only.
/// </summary>
public interface IChatWorkspaceDiffService
{
    Task<ChatWorkspaceSnapshot?> SnapshotAsync(string workspacePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatDeliverableDto>> DiffAsync(
        string workspacePath, ChatWorkspaceSnapshot snapshot, CancellationToken cancellationToken = default);
}
