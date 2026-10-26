using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Delegation;
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
        + "The dispatcher runs it asynchronously; use delegate_status to poll. The prompt MUST be a self-contained brief: goal + context + spec reference (SPEC-... RF-xx/AC-xx when one exists) + acceptance criteria + report format.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "prompt":{"type":"string","description":"Instructions for the agent"},
          "cli":{"type":"string","description":"Target CLI (builtin name or custom def); defaults to the conversation's picked CLI"},
          "depends_on":{"type":"array","items":{"type":"string"},"description":"Task ids that must complete first"},
          "retry_of":{"type":"string","description":"Task id to retry (must be failed/stale/cancelled)"},
          "use_worktree":{"type":"boolean","description":"Run inside a fresh git worktree (requires repository_path)"},
          "repository_path":{"type":"string","description":"Git repo path (absolute or workspace-relative) — enables worktree isolation and the stale-base guard"},
          "base_branch":{"type":"string","description":"Base branch for the worktree (default: repo default)"},
          "model":{"type":"string","description":"Requested launch model — resolved against the CLI's capability"},
          "effort":{"type":"string","description":"Requested launch effort: low|normal|high"},
          "autonomy":{"type":"string","enum":["plan","edit","full"],"description":"Permission envelope: plan=read-only, edit=workspace writes (default), full=unrestricted — full requires a worktree + server opt-in"},
          "refine":{"type":"string","enum":["off","template","llm"],"description":"Prompt-refinement override for this task (default: server config Taskboard:Delegation:PromptRefinement)"},
          "role":{"type":"string","enum":["orchestrator","leaf"],"description":"Contract role: orchestrator may produce child tasks; leaf cannot delegate further (default: leaf; coordinate tasks are always orchestrators)"},
          "output_schema":{"type":"string","description":"JSON Schema string the worker's final reply must satisfy — the completion result is annotated schema_valid (false keeps the full child output)"},
          "dry_run":{"type":"boolean","description":"Resolve the effective config and run preflight checks without persisting the task or dispatching (returns resolved + verdict + checks)"}
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
            var resolved = await ResolveRepoAsync(rawRepo, useWorktree, context, cancellationToken)
                .ConfigureAwait(false);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            repoPath = resolved.RepoPath;
            baseSha = resolved.BaseSha;
        }

        var dependsOn = ReadStringList(arguments, "depends_on");
        var retryOf = ReadString(arguments, "retry_of");
        var model = ReadString(arguments, "model");
        var effort = ReadString(arguments, "effort");
        var autonomy = ReadString(arguments, "autonomy");
        if (autonomy is not null
            && !Taskboard.Agents.AgentAutonomyNames.IsValidName(autonomy.Trim().ToLowerInvariant()))
        {
            return DelegationToolSupport.Error("autonomy must be plan|edit|full", "invalid autonomy");
        }

        var refine = ReadString(arguments, "refine")?.Trim().ToLowerInvariant();
        if (refine is not null
            && !Taskboard.Application.Contracts.Delegation.TaskPromptRefinementModes
                .IsValidOverride(refine))
        {
            return DelegationToolSupport.Error("refine must be off|template|llm", "invalid refine");
        }

        // SPEC-20261024-p1-agent-reliability RF-G01/G02: contractual role +
        // output schema — both validated up-front; the entity re-checks.
        var role = ReadString(arguments, "role")?.Trim().ToLowerInvariant();
        if (role is not null && !DelegationTaskRoles.IsValidName(role))
        {
            return DelegationToolSupport.Error("role must be orchestrator|leaf", "invalid role");
        }

        var outputSchema = ReadString(arguments, "output_schema")?.Trim();
        if (outputSchema is not null && !IsJson(outputSchema))
        {
            return DelegationToolSupport.Error(
                "output_schema must be a valid JSON schema", "invalid output_schema");
        }

        var scope = DelegationToolSupport.ScopeOf(context);

        // SPEC-20261020-v2 RF-008: resolve launch prefs up-front — unknown
        // capability is an explicit error, never silently dropped.
        LaunchResolution? launch = null;
        if (model is not null || effort is not null)
        {
            try
            {
                await using var resolveScope = scopeFactory.CreateAsyncScope();
                var resolver = resolveScope.ServiceProvider
                    .GetRequiredService<IAgentLaunchResolver>();
                launch = await resolver.ResolveLaunchAsync(cli, model, effort, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DomainException ex)
            {
                return DelegationToolSupport.Error(ex.Message, "launch preference refused");
            }
        }

        // SPEC-20261023 RF-C03: dry_run resolves config + runs preflight —
        // no task persisted, no dispatch.
        if (ReadBool(arguments, "dry_run"))
        {
            await using var dryScope = scopeFactory.CreateAsyncScope();
            var report = await dryScope.ServiceProvider
                .GetRequiredService<IPreflightService>()
                .RunAsync(cancellationToken).ConfigureAwait(false);
            return DelegationToolSupport.Ok(new
            {
                dryRun = true,
                resolved = new
                {
                    cli,
                    scope,
                    repositoryPath = repoPath,
                    useWorktree,
                    baseSha,
                    model,
                    effort,
                    autonomy,
                    refine,
                    role,
                    outputSchema,
                    launch = launch is null
                        ? null
                        : new { model = launch.EffectiveModel, effort = launch.EffectiveEffort, launch.Args },
                },
                verdict = report.Verdict,
                checks = report.Checks,
            });
        }

        if (retryOf is not null)
        {
            // SPEC-20261020-v2 RF-007: retry = attempt+1 on the same task.
            try
            {
                await using var retryScope = scopeFactory.CreateAsyncScope();
                var retryService = retryScope.ServiceProvider.GetRequiredService<IDelegationService>();
                var retried = await retryService.RetryTaskAsync(
                        scope, retryOf, refine, launch?.EffectiveModel, cancellationToken)
                    .ConfigureAwait(false);
                context.Activity?.Report("delegated_task", retried.Task.Id);
                return DelegationToolSupport.Ok(new
                {
                    taskId = retried.Task.Id,
                    status = retried.Task.Status.ToString().ToLowerInvariant(),
                    cli = retried.Task.CliName,
                    scope = retried.Task.Scope,
                    dispatchId = retried.Dispatch.Id,
                    attempt = retried.Dispatch.Attempt,
                    retried = true,
                });
            }
            catch (DomainException ex)
            {
                return DelegationToolSupport.Error(ex.Message, "retry refused");
            }
        }

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
                    repoPath, baseSha, Kind: null, RunId: null, model, effort,
                    ExtraArgs: null, Autonomy: autonomy, Refine: refine,
                    Role: role, OutputSchema: outputSchema),
                cancellationToken).ConfigureAwait(false);

            if (useWorktree)
            {
                if (repoPath is null)
                {
                    return DelegationToolSupport.Error("repository_path could not be resolved", "no repo path");
                }

                var attach = await AttachWorktreeAsync(
                    diScope.ServiceProvider.GetService<IWorkspaceIsolationService>(),
                    service, task, repoPath, arguments, cancellationToken).ConfigureAwait(false);
                if (attach.Error is not null)
                {
                    return attach.Error;
                }

                task = attach.Task ?? task;
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
            runId = task.RunId,
            activeDispatchId = task.ActiveDispatchId,
            dependsOn = task.DependsOn,
            retryOf = task.RetryOf,
            useWorktree = task.UseWorktree,
            worktreeRunId = task.WorktreeRunId,
            role = task.Role,
            outputSchema = task.OutputSchema,
            launch = launch is null
                ? null
                : new
                {
                    requested = new { model, effort },
                    effective = new { model = launch.EffectiveModel, effort = launch.EffectiveEffort },
                },
        });
    }

    private async Task<RepoResult> ResolveRepoAsync(
        string? rawRepo, bool useWorktree, ChatToolContext context, CancellationToken cancellationToken)
    {
        var repoPath = await DelegationToolSupport.ResolveGitRootAsync(
            git, rawRepo, context.WorkspacePath, cancellationToken).ConfigureAwait(false);
        if (repoPath is null)
        {
            return new(null, null, DelegationToolSupport.Error(
                "repository_path is not inside a git checkout", "not a git repo"));
        }

        // Worktree legs compare diffs against the base branch — the
        // stale-base guard only applies to shared-checkout tasks.
        var baseSha = useWorktree
            ? null
            : await DelegationToolSupport.HeadShaAsync(git, repoPath, cancellationToken)
                .ConfigureAwait(false);
        return new(repoPath, baseSha, null);
    }

    private static async Task<AttachResult> AttachWorktreeAsync(
        IWorkspaceIsolationService? isolation, IDelegationService service,
        DelegationTaskDto task, string repoPath, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var (session, attached, error) = await DelegationToolSupport
            .ProvisionAndAttachAsync(
                isolation, service, task, repoPath,
                ReadString(arguments, "base_branch") ?? "main",
                "delegation", cancellationToken).ConfigureAwait(false);
        return error is null
            ? new(attached, null)
            : new(null, DelegationToolSupport.Error(error,
                error == "worktree service unavailable" ? "no isolation" : "worktree error"));
    }

    private sealed record RepoResult(string? RepoPath, string? BaseSha, ChatToolResult? Error);

    private sealed record AttachResult(DelegationTaskDto? Task, ChatToolResult? Error);

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static bool ReadBool(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static bool IsJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
