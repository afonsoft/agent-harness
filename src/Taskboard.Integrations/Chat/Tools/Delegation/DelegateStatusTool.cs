using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Dtos;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261005 RF-009: lists delegation tasks of the conversation scope —
/// filter by <c>task_id</c>, <c>group_id</c> or <c>status</c>.
/// </summary>
public sealed class DelegateStatusTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_status";
    public string Description =>
        "Show the status of delegated tasks in this conversation: pending/ready/"
        + "running/done/failed/stale/cancelled with results, errors and heartbeats. "
        + "Filter by task_id, group_id (fan-out) or status.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "task_id":{"type":"string","description":"One task id"},
          "group_id":{"type":"string","description":"Fan-out group id"},
          "status":{"type":"string","description":"Filter by status (pending|ready|running|done|failed|stale|cancelled)"},
          "take":{"type":"integer","description":"Max tasks to return (default 50)"}
        }}
        """;

    public string CapabilityId => "agent:delegate_status";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var scope = DelegationToolSupport.ScopeOf(context);
        var take = arguments.TryGetProperty("take", out var t) && t.ValueKind == JsonValueKind.Number
            ? Math.Clamp(t.GetInt32(), 1, 200)
            : 50;

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var tasks = await service.ListTasksAsync(scope, take, cancellationToken).ConfigureAwait(false);

        if (ReadString(arguments, "task_id") is { } taskId)
        {
            tasks = tasks.Where(x => string.Equals(x.Id, taskId, StringComparison.Ordinal)).ToList();
        }

        if (ReadString(arguments, "group_id") is { } groupId)
        {
            tasks = tasks.Where(x => string.Equals(x.FanoutGroupId, groupId, StringComparison.Ordinal)).ToList();
        }

        if (ReadString(arguments, "status") is { } status)
        {
            tasks = tasks.Where(x =>
                string.Equals(x.Status.ToString(), status, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return DelegationToolSupport.Ok(new
        {
            scope,
            tasks = tasks.Select(x => new
            {
                taskId = x.Id,
                x.CliName,
                status = x.Status.ToString().ToLowerInvariant(),
                x.DependsOn,
                x.RetryOf,
                x.FanoutGroupId,
                x.UseWorktree,
                x.WorktreeRunId,
                x.ResultSummary,
                x.Error,
                x.LastHeartbeatAt,
            }),
        });
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
