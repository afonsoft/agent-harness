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
/// SPEC-20261007 RF-002: coordinator primitive — validates a whole task plan
/// up-front (deps are indices into the same <c>tasks</c> array) and creates the
/// tasks in order; a mid-plan failure cancels everything already created so a
/// broken plan never leaves a half-DAG running.
/// </summary>
public sealed class DelegatePlanTool(
    IServiceScopeFactory scopeFactory,
    IGitCommandRunner git) : IChatTool
{
    public string Name => "delegate_plan";
    public string Description =>
        "Create a multi-task delegation plan in one call: each task gets a "
        + "prompt, an optional CLI and optional deps (indices of earlier tasks "
        + "in this same array it must wait for). Use when you split work into "
        + "ordered steps with dependencies instead of one big prompt.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "tasks":{"type":"array","description":"1-10 task specs","items":{
            "type":"object","properties":{
              "prompt":{"type":"string","description":"Task instructions for the CLI"},
              "cli":{"type":"string","description":"Agent CLI (builtin or custom def); omit for the chat's default"},
              "deps":{"type":"array","items":{"type":"integer"},"description":"Indices of earlier tasks this one depends on"},
              "use_worktree":{"type":"boolean","description":"Isolate the task in a git worktree (default false)"}
            },"required":["prompt"]}},
          "repository_path":{"type":"string","description":"Git repo path — required when any task uses worktree isolation"},
          "base_branch":{"type":"string","description":"Base branch for worktrees (default: repo default)"}
        },"required":["tasks"]}
        """;

    public string CapabilityId => "agent:delegate_plan";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    private const int MaxTasks = 10;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.DelegationDepth > 0)
        {
            return DelegationToolSupport.Error("delegation not allowed inside a sub-agent", "recursion blocked");
        }

        if (!arguments.TryGetProperty("tasks", out var tasksEl)
            || tasksEl.ValueKind != JsonValueKind.Array)
        {
            return DelegationToolSupport.Error("tasks array is required", "missing tasks");
        }

        var specs = new List<PlanTask>();
        var index = 0;
        foreach (var el in tasksEl.EnumerateArray())
        {
            var prompt = ReadString(el, "prompt");
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return DelegationToolSupport.Error($"tasks[{index}].prompt is required", "empty prompt");
            }

            var deps = ReadDeps(el);
            if (deps is null)
            {
                return DelegationToolSupport.Error(
                    $"tasks[{index}].deps must be integers", "bad dep index");
            }

            specs.Add(new PlanTask(
                prompt,
                ReadString(el, "cli"),
                deps,
                el.TryGetProperty("use_worktree", out var uw) && uw.ValueKind == JsonValueKind.True));
            index++;
        }

        if (specs.Count == 0)
        {
            return DelegationToolSupport.Error("tasks array is empty", "empty plan");
        }

        if (specs.Count > MaxTasks)
        {
            return DelegationToolSupport.Error($"delegate_plan supports at most {MaxTasks} tasks", "too many tasks");
        }

        for (var i = 0; i < specs.Count; i++)
        {
            foreach (var dep in specs[i].Deps)
            {
                if (dep >= i)
                {
                    return DelegationToolSupport.Error(
                        $"tasks[{i}].deps must point to earlier tasks (got {dep})", "bad dep index");
                }
            }
        }

        var needsWorktree = specs.Any(s => s.UseWorktree);
        var rawRepo = ReadString(arguments, "repository_path");
        if (needsWorktree && string.IsNullOrWhiteSpace(rawRepo))
        {
            return DelegationToolSupport.Error(
                "worktree tasks need repository_path", "missing repository");
        }

        string? repoPath = null;
        if (needsWorktree || rawRepo is not null)
        {
            repoPath = await DelegationToolSupport.ResolveGitRootAsync(
                git, rawRepo, context.WorkspacePath, cancellationToken).ConfigureAwait(false);
            if (repoPath is null)
            {
                return DelegationToolSupport.Error(
                    "repository_path is not inside a git checkout", "not a git repo");
            }
        }

        var scope = DelegationToolSupport.ScopeOf(context);
        var created = new List<object>();
        var createdIds = new List<string>();
        IDelegationService? service = null;

        try
        {
            await using var diScope = scopeFactory.CreateAsyncScope();
            service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
            var isolation = diScope.ServiceProvider.GetService<IWorkspaceIsolationService>();
            var baseBranch = ReadString(arguments, "base_branch") ?? "main";

            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var dependsOn = spec.Deps.Count == 0
                    ? null
                    : spec.Deps.Select(d => createdIds[d]).ToList();
                var cli = string.IsNullOrWhiteSpace(spec.Cli)
                    ? context.DefaultAgentCli ?? "opencode"
                    : spec.Cli!.Trim();

                var task = await service.CreateTaskAsync(
                    new CreateDelegationTaskRequest(
                        spec.Prompt, cli, scope, context.WorkspacePath,
                        dependsOn, RetryOf: null, FanoutGroupId: null,
                        spec.UseWorktree, repoPath, BaseCommitSha: null),
                    cancellationToken).ConfigureAwait(false);

                var status = "pending";
                if (spec.UseWorktree)
                {
                    if (isolation is null)
                    {
                        throw new InvalidOperationException("worktree service unavailable");
                    }

                    var session = await isolation.CreateWorktreeAsync(
                        task.Id, repoPath!, baseBranch,
                        $"plan-{task.Id[^Math.Min(8, task.Id.Length)..]}",
                        retainOnFailure: false, cancellationToken).ConfigureAwait(false);
                    task = (await service.AttachWorktreeAsync(task.Id, session.RunId, cancellationToken)
                        .ConfigureAwait(false)) ?? task;
                }

                createdIds.Add(task.Id);
                created.Add(new
                {
                    index = i,
                    id = task.Id,
                    cli = task.CliName,
                    status,
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (service is not null)
            {
                foreach (var id in createdIds)
                {
                    try
                    {
                        await service.CancelTaskAsync(id, "plan-aborted", CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (DomainException)
                    {
                        // Best-effort rollback — a task that already started keeps running.
                    }
                }
            }

            var message = ex is DomainException ? ex.Message : $"plan aborted: {ex.Message}";
            return DelegationToolSupport.Error(message, "plan-aborted");
        }

        context.Activity?.Report("delegated_plan", $"{created.Count} tasks");
        return DelegationToolSupport.Ok(new { created, scope });
    }

    private sealed record PlanTask(
        string Prompt, string? Cli, IReadOnlyList<int> Deps, bool UseWorktree);

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static IReadOnlyList<int>? ReadDeps(JsonElement el)
    {
        if (!el.TryGetProperty("deps", out var p) || p.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var deps = new List<int>();
        foreach (var dep in p.EnumerateArray())
        {
            if (dep.ValueKind != JsonValueKind.Number
                || !dep.TryGetInt32(out var value))
            {
                return null;
            }

            deps.Add(value);
        }

        return deps;
    }
}
