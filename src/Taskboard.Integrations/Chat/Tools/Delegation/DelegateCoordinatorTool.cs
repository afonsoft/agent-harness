using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Taskboard.Integrations.Delegation;
using Taskboard.Integrations.Harness;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261009 RF-001: creates a <c>coordinate</c> delegation task — the
/// dispatcher runs the goal through the planner CLI wrapped in a strict-JSON
/// preamble, then materializes the returned plan as child tasks in the same
/// scope. The autonomous counterpart of delegate_plan.
/// </summary>
public sealed class DelegateCoordinatorTool(
    IServiceScopeFactory scopeFactory,
    IGitCommandRunner git) : IChatTool
{
    public string Name => "delegate_coordinate";
    public string Description =>
        "Delegate a whole goal to a coordinator agent: it plans the work and "
        + "spawns the child tasks itself. Use for multi-step jobs where you want "
        + "an agent to decide the decomposition instead of handing it a fixed plan.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "goal":{"type":"string","description":"What to accomplish — the coordinator plans and delegates it"},
          "cli":{"type":"string","description":"Planner CLI (builtin or custom def); defaults to the conversation's picked CLI"},
          "max_tasks":{"type":"integer","description":"Upper bound of child tasks (default 6, max 10)"},
          "repository_path":{"type":"string","description":"Git repo path — lets the plan use worktree isolation"}
        },"required":["goal"]}
        """;

    public string CapabilityId => "agent:delegate_coordinate";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => true;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.DelegationDepth > 0)
        {
            return DelegationToolSupport.Error(
                "delegation not allowed inside a sub-agent", "recursion blocked");
        }

        var goal = ReadString(arguments, "goal");
        if (string.IsNullOrWhiteSpace(goal))
        {
            return DelegationToolSupport.Error("goal is required", "empty goal");
        }

        var cli = ReadString(arguments, "cli") ?? context.DefaultAgentCli;
        if (string.IsNullOrWhiteSpace(cli))
        {
            return DelegationToolSupport.Error(
                "no cli given and no default agent picked in the Agent bar", "missing cli");
        }

        var maxTasks = DelegationPlanCreator.MaxTasks;
        if (arguments.TryGetProperty("max_tasks", out var mt)
            && mt.ValueKind == JsonValueKind.Number
            && mt.TryGetInt32(out var parsed))
        {
            maxTasks = Math.Clamp(parsed, 1, DelegationPlanCreator.MaxTasks);
        }

        string? repoPath = null;
        var rawRepo = ReadString(arguments, "repository_path");
        if (rawRepo is not null)
        {
            repoPath = await DelegationToolSupport.ResolveGitRootAsync(
                git, rawRepo, context.WorkspacePath, cancellationToken).ConfigureAwait(false);
            if (repoPath is null)
            {
                return DelegationToolSupport.Error(
                    "repository_path is not inside a git checkout", "not a git repo");
            }
        }

        // The stored prompt is the goal plus the task-count hint; the dispatcher
        // wraps it in the strict-JSON preamble before handing it to the planner.
        var prompt = maxTasks < DelegationPlanCreator.MaxTasks
            ? $"Decompose this goal into at most {maxTasks} tasks.\n\n{goal}"
            : goal;
        var scope = DelegationToolSupport.ScopeOf(context);

        DelegationTaskDto task;
        try
        {
            await using var diScope = scopeFactory.CreateAsyncScope();
            var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
            task = await service.CreateTaskAsync(
                new CreateDelegationTaskRequest(
                    prompt, cli, scope, context.WorkspacePath,
                    DependsOn: null, RetryOf: null, FanoutGroupId: null,
                    UseWorktree: false, repoPath, BaseCommitSha: null,
                    Kind: DelegationTaskKinds.Coordinate),
                cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "invalid task");
        }

        context.Activity?.Report("delegated_coordinate", task.Id);
        return DelegationToolSupport.Ok(new
        {
            taskId = task.Id,
            kind = task.Kind,
            status = task.Status.ToString().ToLowerInvariant(),
            cli = task.CliName,
            scope = task.Scope,
            maxTasks,
        });
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
