using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261005 RF-007: compares the worktree diffs of fan-out legs — files
/// changed, insertions/deletions and a bounded patch per leg.
/// </summary>
public sealed class DelegateCompareTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    private const int PatchMaxLength = 8000;

    public string Name => "delegate_compare";
    public string Description =>
        "Compare the diffs produced by fan-out legs: per task, the changed "
        + "files, insertions/deletions and a truncated patch vs the worktree "
        + "base. Tasks without a worktree report no diff.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "group_id":{"type":"string","description":"Fan-out group id"},
          "task_ids":{"type":"array","items":{"type":"string"},"description":"Explicit task ids to compare"}
        }}
        """;

    public string CapabilityId => "agent:delegate_compare";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var scope = DelegationToolSupport.ScopeOf(context);
        var groupId = ReadString(arguments, "group_id");
        var taskIds = ReadStringList(arguments, "task_ids");
        if (groupId is null && taskIds.Count == 0)
        {
            return DelegationToolSupport.Error("group_id or task_ids is required", "missing selector");
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var isolation = diScope.ServiceProvider.GetService<IWorkspaceIsolationService>();
        var tasks = await service.ListTasksAsync(scope, take: 200, cancellationToken).ConfigureAwait(false);

        var selected = tasks.Where(t =>
            (groupId is not null && string.Equals(t.FanoutGroupId, groupId, StringComparison.Ordinal))
            || taskIds.Contains(t.Id)).ToList();
        if (selected.Count == 0)
        {
            return DelegationToolSupport.Error("no tasks matched", "empty selection");
        }

        var legs = new List<object>();
        foreach (var task in selected)
        {
            if (task.WorktreeRunId is null || isolation is null)
            {
                legs.Add(new
                {
                    taskId = task.Id,
                    task.CliName,
                    status = task.Status.ToString().ToLowerInvariant(),
                    files = (object?)null,
                    note = "no worktree",
                });
                continue;
            }

            try
            {
                var diff = await isolation.GetDiffAsync(task.WorktreeRunId, cancellationToken)
                    .ConfigureAwait(false);
                legs.Add(new
                {
                    taskId = task.Id,
                    task.CliName,
                    status = task.Status.ToString().ToLowerInvariant(),
                    files = new
                    {
                        diff.FilesChanged,
                        diff.Insertions,
                        diff.Deletions,
                        paths = diff.Files.Select(f => new { f.Path, f.Status, f.Insertions, f.Deletions }),
                    },
                    patch = diff.Patch.Length <= PatchMaxLength
                        ? diff.Patch
                        : diff.Patch[..PatchMaxLength] + "…",
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                legs.Add(new
                {
                    taskId = task.Id,
                    task.CliName,
                    status = task.Status.ToString().ToLowerInvariant(),
                    files = (object?)null,
                    note = $"diff failed: {ex.Message}",
                });
            }
        }

        return DelegationToolSupport.Ok(new { scope, groupId, legs });
    }

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
