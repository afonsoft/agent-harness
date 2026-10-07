using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Dtos;
using Taskboard.ValueObjects;
using Taskboard.Repositories;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261011-chat-workspace-panel: resolves the workspace a chat
/// conversation's workspace panel operates on — the latest run's live
/// worktree when it has one, else the conversation workspace (explicit path,
/// then the bound repository clone, then the workspace root). Feeds the
/// workspace/todos/diff/plan endpoints and the conversation-scoped PTY.
/// </summary>
public sealed class ConversationWorkspaceService(
    IRepository<ChatConversation> conversations,
    IRepository<ChatRun> runs,
    IRepository<ChatApproval> approvals,
    IWorktreeSessionRepository worktreeSessions,
    IWorkspaceIsolationService isolation,
    IChatWorkspaceDiffService workspaceDiff,
    IWorkspacePathResolver workspace,
    ChatTodoStore todos)
{
    /// <summary>RF-003: path + git status of the conversation's workspace.</summary>
    public async Task<ConversationWorkspaceDto?> GetWorkspaceAsync(
        string conversationId, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return null;
        }

        var (conversation, path, worktreeRunId) = resolved.Value;
        var status = path is null
            ? null
            : await workspaceDiff.StatusAsync(path, cancellationToken).ConfigureAwait(false);
        return new ConversationWorkspaceDto(
            path,
            conversation.RepositoryFullName,
            IsGit: status is not null,
            status?.Branch,
            Dirty: status?.Dirty ?? false,
            worktreeRunId);
    }

    /// <summary>RF-004: todo items written by the run's <c>todo</c> tool.</summary>
    public async Task<IReadOnlyList<ChatTodoItem>?> GetTodosAsync(
        string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations
            .GetAsync(ChatConversationId.From(conversationId), cancellationToken)
            .ConfigureAwait(false);
        return conversation is null ? null : todos.List(conversationId);
    }

    /// <summary>
    /// RF-005: workspace diff — worktree diff vs its base branch when the
    /// latest run has a worktree, else workspace changes vs HEAD.
    /// </summary>
    public async Task<WorkspaceDiffDto?> GetDiffAsync(
        string conversationId, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return null;
        }

        var (_, path, worktreeRunId) = resolved.Value;
        if (worktreeRunId is not null)
        {
            return await isolation.GetDiffAsync(worktreeRunId, cancellationToken).ConfigureAwait(false);
        }

        return path is null
            ? null
            : await workspaceDiff.CurrentDiffAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>RF-008: the latest plan-review approval's plan markdown.</summary>
    public async Task<ChatApprovalDto?> GetLatestPlanAsync(
        string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations
            .GetAsync(ChatConversationId.From(conversationId), cancellationToken)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        var approval = await approvals.Query
            .Where(a => a.ConversationId == conversation.Id && a.Kind == ChatApprovalKind.PlanReview)
            .OrderByDescending(a => a.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (approval is null)
        {
            return null;
        }

        return new ChatApprovalDto(
            approval.Id.Value,
            approval.RunId.Value,
            approval.ConversationId.Value,
            approval.ToolCallId,
            approval.ToolName,
            approval.ArgumentsPreview,
            approval.Status.Value,
            approval.RequestedAt,
            approval.DecidedAt,
            approval.Decision,
            approval.DecidedBy?.Value,
            approval.Kind.Value,
            approval.Risk,
            approval.RiskReason);
    }

    /// <summary>RF-006: workdir for the conversation-scoped PTY (<c>conv-&lt;id&gt;</c>).</summary>
    public async Task<string?> ResolveWorkdirAsync(
        string conversationId, CancellationToken cancellationToken = default)
        => (await ResolveAsync(conversationId, cancellationToken).ConfigureAwait(false))?.Path;

    private async Task<(ChatConversation Conversation, string? Path, string? WorktreeRunId)?> ResolveAsync(
        string conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations
            .GetAsync(ChatConversationId.From(conversationId), cancellationToken)
            .ConfigureAwait(false);
        if (conversation is null)
        {
            return null;
        }

        // The latest run's live worktree wins — the panel mirrors what the
        // agent touched last. Chat runs don't create worktrees today, but
        // delegated runs under the conversation can.
        var latestRunId = await runs.Query
            .Where(r => r.ConversationId == conversation.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Id.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (latestRunId is not null)
        {
            var session = await worktreeSessions
                .GetByRunIdAsync(latestRunId, cancellationToken)
                .ConfigureAwait(false);
            if (session is not null && Directory.Exists(session.Path))
            {
                return (conversation, session.Path, latestRunId);
            }
        }

        return (conversation, ResolveConversationPath(conversation), null);
    }

    // Same fallback as the tool-call context (ChatService ~2680): explicit
    // workspace path, then the bound repo clone, else the workspace root.
    private string ResolveConversationPath(ChatConversation conversation)
        => !string.IsNullOrWhiteSpace(conversation.WorkspacePath)
            ? conversation.WorkspacePath
            : workspace.ResolveCardWorkdir(conversation.RepositoryFullName, out _);
}
