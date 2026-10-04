using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Integrations.Harness;
using Taskboard;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261005 RF-009: creates one <c>DelegationTask</c> in the conversation
/// DAG — pending until its deps finish, then dispatched by the delegation
/// dispatcher. Supports <c>depends_on</c>, <c>retry_of</c>, worktree isolation
/// and the stale-base guard.
/// </summary>
public sealed class DelegateTaskTool(
    IServiceScopeFactory scopeFactory,
    IGitCommandRunner git) : IChatTool
{
    public string Name => "delegate_task";
    public string Description =>
        "Create a delegated task for an agent CLI in the current conversation's "
        + "task DAG. Use depends_on to sequence tasks (a task only runs after "
        + "its dependencies complete) and retry_of to retry a failed/stale task. "
        + "The dispatcher runs it asynchronously; use delegate_status to poll.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "prompt":{"type":"string","description":"Instructions for the agent"},
          "cli":{"type":"string","description":"Target CLI (builtin name or custom def); defaults to the conversation's picked CLI"},
          "depends_on":{"type":"array","items":{"type":"string"},"description":"Task ids that must complete first"},
          "retry_of":{"type":"string","description":"Task id to retry (must be failed/stale/cancelled)"},
          "use_worktree":{"type":"boolean","description":"Run inside a fresh git worktree (requires repository_path)"},
          "repository_path":{"type":"string","description":"Git repo path (absolute or workspace-relative) — enables worktree isolation and the stale-base guard"},
          "base_branch":{"type":"string","description":"Base branch for the worktree (default: repo default)"}
        },"required":["prompt"]}
        """;

    public string CapabilityId => "agent:delegate_task";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.DelegationDepth > 0)
        {
            return DelegationToolSupport.Error("delegation not allowed inside a sub-agent", "recursion blocked");
        }

        var prompt = ReadString(arguments, "prompt");
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return DelegationToolSupport.Error("prompt is required", "empty prompt");
        }

        var cli = ReadString(arguments, "cli") ?? context.DefaultAgentCli;
        if (string.IsNullOrWhiteSpace(cli))
        {
            return DelegationToolSupport.Error(
                "no cli given and no default agent picked in the Agent bar", "missing cli");
        }

        var useWorktree = ReadBool(arguments, "use_worktree");
        var rawRepo = ReadString(arguments, "repository_path");
        string? repoPath = null;
        string? baseSha = null;

        if (useWorktree || rawRepo is not null)
        {
            repoPath = await DelegationToolSupport.ResolveGitRootAsync(
                git, rawRepo, context.WorkspacePath, cancellationToken).ConfigureAwait(false);
            if (repoPath is null)
            {
                return DelegationToolSupport.Error(
                    "repository_path is not inside a git checkout", "not a git repo");
            }

            if (useWorktree)
            {
                // Worktree legs compare diffs against the base branch — the
                // stale-base guard only applies to shared-checkout tasks.
            }
            else
            {
                baseSha = await DelegationToolSupport.HeadShaAsync(git, repoPath, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var dependsOn = ReadStringList(arguments, "depends_on");
        var retryOf = ReadString(arguments, "retry_of");
        var scope = DelegationToolSupport.ScopeOf(context);

        DelegationTaskDto task;
        try
        {
            await using var diScope = scopeFactory.CreateAsyncScope();
            var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
            task = await service.CreateTaskAsync(
                new CreateDelegationTaskRequest(
                    prompt, cli, scope, context.WorkspacePath,
                    dependsOn.Count == 0 ? null : dependsOn,
                    retryOf, FanoutGroupId: null, useWorktree,
                    repoPath, baseSha),
                cancellationToken).ConfigureAwait(false);

            if (useWorktree)
            {
                var isolation = diScope.ServiceProvider.GetService<IWorkspaceIsolationService>();
                if (isolation is null)
                {
                    await service.CancelTaskAsync(task.Id, "worktree service unavailable", cancellationToken)
                        .ConfigureAwait(false);
                    return DelegationToolSupport.Error("worktree service unavailable", "no isolation");
                }

                try
                {
                    var session = await isolation.CreateWorktreeAsync(
                        task.Id, repoPath!, ReadString(arguments, "base_branch") ?? "main",
                        $"delegation-{task.Id[^Math.Min(8, task.Id.Length)..]}",
                        retainOnFailure: false, cancellationToken).ConfigureAwait(false);
                    task = (await service.AttachWorktreeAsync(task.Id, session.RunId, cancellationToken)
                        .ConfigureAwait(false)) ?? task;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await service.CancelTaskAsync(task.Id, $"worktree create failed: {ex.Message}", cancellationToken)
                        .ConfigureAwait(false);
                    return DelegationToolSupport.Error(
                        $"worktree create failed: {ex.Message}", "worktree error");
                }
            }
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "invalid task");
        }

        context.Activity?.Report("delegated_task", task.Id);
        return DelegationToolSupport.Ok(new
        {
            taskId = task.Id,
            status = task.Status.ToString().ToLowerInvariant(),
            cli = task.CliName,
            scope = task.Scope,
            dependsOn = task.DependsOn,
            retryOf = task.RetryOf,
            useWorktree = task.UseWorktree,
            worktreeRunId = task.WorktreeRunId,
        });
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static bool ReadBool(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<string> ReadStringList(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return p.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }
}
