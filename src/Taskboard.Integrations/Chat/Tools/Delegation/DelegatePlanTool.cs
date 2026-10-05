using System.Text.Json;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Dtos;
using Taskboard.Integrations.Delegation;
using Taskboard.Integrations.Harness;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261007 RF-002: coordinator primitive — validates a whole task plan
/// up-front (deps are indices into the same <c>tasks</c> array) and materializes
/// it via <see cref="DelegationPlanCreator"/>; a mid-plan failure cancels
/// everything already created so a broken plan never leaves a half-DAG running.
/// </summary>
public sealed class DelegatePlanTool(
    DelegationPlanCreator planCreator,
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

        var specs = DelegationPlanParser.ParseTasks(
            tasksEl, DelegationPlanCreator.MaxTasks, out var parseError);
        if (parseError is not null)
        {
            return DelegationToolSupport.Error(parseError, "bad plan");
        }

        if (specs is null)
        {
            return DelegationToolSupport.Error("could not parse tasks", "bad plan");
        }

        var repoError = await ResolveRepoErrorAsync(
            specs, arguments, context, cancellationToken).ConfigureAwait(false);
        if (repoError.Error is not null)
        {
            return repoError.Error;
        }

        var scope = DelegationToolSupport.ScopeOf(context);
        var baseBranch = DelegationPlanParser.ReadString(arguments, "base_branch") ?? "main";

        try
        {
            var created = await planCreator.CreateAsync(
                specs, repoError.RepoPath, baseBranch, scope, context.WorkspacePath,
                context.DefaultAgentCli, cancellationToken).ConfigureAwait(false);
            context.Activity?.Report("delegated_plan", $"{created.Count} tasks");
            return DelegationToolSupport.Ok(new
            {
                created = created.Select(c => new { index = c.Index, id = c.Id, cli = c.Cli, status = "pending" }),
                scope,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = ex is DomainException ? ex.Message : $"plan aborted: {ex.Message}";
            return DelegationToolSupport.Error(message, "plan-aborted");
        }
    }

    private async Task<RepoResult> ResolveRepoErrorAsync(
        List<DelegationPlanSpec> specs, JsonElement arguments, ChatToolContext context,
        CancellationToken cancellationToken)
    {
        var rawRepo = DelegationPlanParser.ReadString(arguments, "repository_path");
        if (specs.Any(s => s.UseWorktree) && string.IsNullOrWhiteSpace(rawRepo))
        {
            return new(null, DelegationToolSupport.Error(
                "worktree tasks need repository_path", "missing repository"));
        }

        if (!specs.Any(s => s.UseWorktree) && rawRepo is null)
        {
            return new(null, null);
        }

        var repoPath = await DelegationToolSupport.ResolveGitRootAsync(
            git, rawRepo, context.WorkspacePath, cancellationToken).ConfigureAwait(false);
        return repoPath is null
            ? new(null, DelegationToolSupport.Error(
                "repository_path is not inside a git checkout", "not a git repo"))
            : new(repoPath, null);
    }

    private sealed record RepoResult(string? RepoPath, ChatToolResult? Error);
}
