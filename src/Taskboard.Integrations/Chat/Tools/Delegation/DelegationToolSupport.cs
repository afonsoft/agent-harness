using System.Text.Json;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Integrations.Harness;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// Shared helpers of the SPEC-20261005 delegation tools: scope resolution,
/// repo-path/git-base capture, recipient computation for inbox reads and the
/// common error envelopes.
/// </summary>
public static class DelegationToolSupport
{
    public static string ScopeOf(ChatToolContext context) =>
        string.IsNullOrWhiteSpace(context.ConversationId) ? "harness" : context.ConversationId!;

    public static ChatToolResult Error(string message, string reason) =>
        new(JsonSerializer.Serialize(new { error = message }), Refused: true, reason);

    public static ChatToolResult Ok(object payload) =>
        new(JsonSerializer.Serialize(payload));

    /// <summary>Single string-arg reader shared by the delegation tools.</summary>
    public static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>
    /// Resolves a run's worktree session and verifies the directory still
    /// exists — shared by the <c>worktree_*</c> tools that operate on a live
    /// worktree (SonarCloud dedupe: the session+Directory check repeated in
    /// each tool).
    /// </summary>
    public static async Task<(WorktreeSessionDto? Session, ChatToolResult? Error)>
        RequireWorktreeSessionAsync(
            IWorktreeSessionRepository sessions,
            string runId,
            CancellationToken ct)
    {
        var session = await sessions.GetByRunIdAsync(runId, ct).ConfigureAwait(false);
        return session is null || !Directory.Exists(session.Path)
            ? (null, Error("run has no live worktree", "no worktree"))
            : (session, null);
    }

    /// <summary>
    /// Resolves a user-supplied repo path (absolute or workspace-relative) to
    /// the git toplevel; null when the path is not inside a git checkout.
    /// </summary>
    public static async Task<string?> ResolveGitRootAsync(
        IGitCommandRunner git, string? rawPath, string workspace, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var full = Path.IsPathRooted(rawPath)
            ? Path.GetFullPath(rawPath)
            : Path.GetFullPath(Path.Join(workspace, rawPath));

        var res = await git.RunAsync(
            full, ["rev-parse", "--show-toplevel"],
            TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        return res.ExitCode == 0 ? res.StandardOutput.Trim() : null;
    }

    /// <summary>Current HEAD sha of a git repo; null when unavailable.</summary>
    public static async Task<string?> HeadShaAsync(
        IGitCommandRunner git, string repoPath, CancellationToken ct)
    {
        var res = await git.RunAsync(
            repoPath, ["rev-parse", "HEAD"], TimeSpan.FromSeconds(10), ct)
            .ConfigureAwait(false);
        return res.ExitCode == 0 ? res.StandardOutput.Trim() : null;
    }

    /// <summary>
    /// Mailbox recipients the assistant can see: broadcast + idle markers, its
    /// own cli, the scope itself and every task id of the scope.
    /// </summary>
    public static async Task<IReadOnlyCollection<string>> RecipientsAsync(
        IDelegationService service, ChatToolContext context, string scope, CancellationToken ct)
    {
        var recipients = new List<string> { "@all", "@idle", scope };
        if (!string.IsNullOrWhiteSpace(context.DefaultAgentCli))
        {
            recipients.Add(context.DefaultAgentCli!);
        }

        foreach (var task in await service.ListTasksAsync(scope, take: 200, ct).ConfigureAwait(false))
        {
            recipients.Add(task.Id);
            // RF-009: worktree tasks get a group address their dispatch can read.
            if (task.WorktreeRunId is { } wt)
            {
                recipients.Add($"@worktree:{wt}");
            }
        }

        return recipients;
    }
    /// <summary>
    /// SPEC-20261021-worktree-setup-scripts: one-line attach — propagates the
    /// session's shared paths + setup outcome into the task so the worker
    /// preamble can echo them.
    /// </summary>
    public static Task<DelegationTaskDto?> AttachSessionAsync(
        IDelegationService service,
        string taskId,
        WorktreeSessionDto session,
        CancellationToken ct) =>
        service.AttachWorktreeAsync(
            taskId, session.RunId, session.SharedPathsApplied,
            session.SetupState, session.SetupLogTail, ct);

    /// <summary>
    /// Shared worktree provisioning envelope for the delegation tools:
    /// creates the worktree, attaches the session (shared paths + setup
    /// outcome) to the task, and cancels the task on failure instead of
    /// leaking a half-provisioned leg.
    /// </summary>
    public static async Task<(WorktreeSessionDto? Session, DelegationTaskDto Task, string? Error)>
        ProvisionAndAttachAsync(
            IWorkspaceIsolationService? isolation,
            IDelegationService service,
            DelegationTaskDto task,
            string repoPath,
            string baseBranch,
            string slugPrefix,
            CancellationToken ct,
            string? taskSlug = null)
    {
        if (isolation is null)
        {
            await service.CancelTaskAsync(
                task.Id, "worktree service unavailable", ct).ConfigureAwait(false);
            return (null, task, "worktree service unavailable");
        }

        try
        {
            var session = await isolation.CreateWorktreeAsync(
                task.Id, repoPath, baseBranch,
                taskSlug ?? $"{slugPrefix}-{task.Id[^Math.Min(8, task.Id.Length)..]}",
                retainOnFailure: false, ct).ConfigureAwait(false);
            var attached = (await AttachSessionAsync(
                service, task.Id, session, ct).ConfigureAwait(false)) ?? task;
            return (session, attached, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = $"worktree create failed: {ex.Message}";
            await service.CancelTaskAsync(task.Id, error, ct).ConfigureAwait(false);
            return (null, task, error);
        }
    }
}
