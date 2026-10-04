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
/// SPEC-20261005 RF-006: fans one prompt out to N CLIs, each isolated in its
/// own git worktree of the same repo, sharing a <c>FanoutGroupId</c>. Compare
/// the legs afterwards with <c>delegate_compare</c>.
/// </summary>
public sealed class DelegateFanoutTool(
    IServiceScopeFactory scopeFactory,
    IGitCommandRunner git) : IChatTool
{
    public string Name => "delegate_fanout";
    public string Description =>
        "Run the same prompt with several agent CLIs in parallel, each inside "
        + "its own git worktree of one repository, then compare the diffs with "
        + "delegate_compare. Use when you want competing implementations to "
        + "pick a winner from.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "prompt":{"type":"string","description":"Instructions given to every CLI"},
          "clis":{"type":"array","items":{"type":"string"},"description":"2-6 CLI names (builtins or custom defs)"},
          "repository_path":{"type":"string","description":"Git repo path (absolute or workspace-relative) — required for worktree isolation"},
          "base_branch":{"type":"string","description":"Base branch (default: repo default)"},
          "use_worktree":{"type":"boolean","description":"Isolate each leg in a worktree (default true)"}
        },"required":["prompt","clis"]}
        """;

    public string CapabilityId => "agent:delegate_fanout";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    private const int MaxLegs = 6;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (context.DelegationDepth > 0)
        {
            return DelegationToolSupport.Error("delegation not allowed inside a sub-agent", "recursion blocked");
        }

        if (ValidateArgs(arguments, out var prompt, out var clis, out var useWorktree) is { } argError)
        {
            return argError;
        }

        var repoResult = await ResolveRepoAsync(
            useWorktree, arguments, context, cancellationToken).ConfigureAwait(false);
        if (repoResult.Error is not null)
        {
            return repoResult.Error;
        }

        var scope = DelegationToolSupport.ScopeOf(context);
        var groupId = $"fan-{Guid.NewGuid():N}";

        try
        {
            var legs = await CreateLegsAsync(
                prompt!, clis, useWorktree, repoResult.RepoPath, arguments, scope, groupId,
                context, cancellationToken).ConfigureAwait(false);
            context.Activity?.Report("delegated_fanout", groupId);
            return DelegationToolSupport.Ok(new { groupId, scope, legs });
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "invalid task");
        }
    }

    private static ChatToolResult? ValidateArgs(
        JsonElement arguments, out string? prompt, out IReadOnlyList<string> clis,
        out bool useWorktree)
    {
        prompt = ReadString(arguments, "prompt");
        clis = ReadStringList(arguments, "clis");
        useWorktree = !arguments.TryGetProperty("use_worktree", out var uw)
            || uw.ValueKind is not JsonValueKind.False;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return DelegationToolSupport.Error("prompt is required", "empty prompt");
        }

        if (clis.Count < 2)
        {
            return DelegationToolSupport.Error("fan-out needs at least 2 clis", "too few clis");
        }

        return clis.Count > MaxLegs
            ? DelegationToolSupport.Error($"fan-out supports at most {MaxLegs} clis", "too many clis")
            : null;
    }

    private async Task<RepoResult> ResolveRepoAsync(
        bool useWorktree, JsonElement arguments, ChatToolContext context,
        CancellationToken cancellationToken)
    {
        var rawRepo = ReadString(arguments, "repository_path");
        if (useWorktree && string.IsNullOrWhiteSpace(rawRepo))
        {
            return new(null, DelegationToolSupport.Error(
                "fan-out with worktrees needs repository_path", "missing repository"));
        }

        if (!useWorktree && rawRepo is null)
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

    private async Task<List<object>> CreateLegsAsync(
        string prompt, IReadOnlyList<string> clis, bool useWorktree, string? repoPath,
        JsonElement arguments, string scope, string groupId, ChatToolContext context,
        CancellationToken cancellationToken)
    {
        var legs = new List<object>();
        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var isolation = diScope.ServiceProvider.GetService<IWorkspaceIsolationService>();
        var baseBranch = ReadString(arguments, "base_branch") ?? "main";

        foreach (var cli in clis)
        {
            var task = await service.CreateTaskAsync(
                new CreateDelegationTaskRequest(
                    prompt, cli, scope, context.WorkspacePath,
                    DependsOn: null, RetryOf: null, groupId,
                    useWorktree, repoPath, BaseCommitSha: null),
                cancellationToken).ConfigureAwait(false);

            var leg = await AttachWorktreeAsync(
                task, useWorktree, isolation, service, repoPath, baseBranch, cancellationToken)
                .ConfigureAwait(false);
            legs.Add(leg);
        }

        return legs;
    }

    private static async Task<object> AttachWorktreeAsync(
        DelegationTaskDto task, bool useWorktree, IWorkspaceIsolationService? isolation,
        IDelegationService service, string? repoPath, string baseBranch,
        CancellationToken cancellationToken)
    {
        if (!useWorktree)
        {
            return LegOf(task, null, null);
        }

        if (isolation is null)
        {
            await service.CancelTaskAsync(task.Id, "worktree service unavailable", cancellationToken)
                .ConfigureAwait(false);
            return LegOf(task, null, "worktree service unavailable");
        }

        try
        {
            var session = await isolation.CreateWorktreeAsync(
                task.Id, repoPath!, baseBranch,
                $"fanout-{task.Id[^Math.Min(8, task.Id.Length)..]}",
                retainOnFailure: false, cancellationToken).ConfigureAwait(false);
            task = (await service.AttachWorktreeAsync(task.Id, session.RunId, cancellationToken)
                .ConfigureAwait(false)) ?? task;
            return LegOf(task, session.Path, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = $"worktree create failed: {ex.Message}";
            await service.CancelTaskAsync(task.Id, error, cancellationToken).ConfigureAwait(false);
            return LegOf(task, null, error);
        }
    }

    private static object LegOf(DelegationTaskDto task, string? worktreePath, string? error) =>
        new
        {
            taskId = task.Id,
            cli = task.CliName,
            status = error is null ? "pending" : "cancelled",
            worktreePath,
            error,
        };

    private sealed record RepoResult(string? RepoPath, ChatToolResult? Error);

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

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
