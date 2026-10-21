using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Dtos;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261020-v2 RF-001: inspects a coordination run — tasks, open
/// dispatches and unacknowledged deliveries of the scope's durable run.
/// </summary>
public sealed class DelegateRunShowTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_run_show";
    public string Description =>
        "Show the current delegation run of this conversation: run metadata, "
        + "its tasks with status, open dispatches (attempts in flight) and "
        + "unacknowledged mailbox deliveries.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "run_id":{"type":"string","description":"Run id to inspect (default: the scope's open run)"}
        }}
        """;

    public string CapabilityId => "agent:run_show";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var scope = DelegationToolSupport.ScopeOf(context);
        var runId = arguments.TryGetProperty("run_id", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString()
            : null;

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var show = await service.GetRunShowAsync(runId, scope, cancellationToken)
            .ConfigureAwait(false);
        if (show is null)
        {
            return DelegationToolSupport.Error(
                runId is null ? "no open run in this scope" : $"unknown run: {runId}",
                "run not found");
        }

        return DelegationToolSupport.Ok(new
        {
            run = new
            {
                id = show.Run.Id,
                show.Run.Scope,
                show.Run.Objective,
                status = show.Run.Status.ToString().ToLowerInvariant(),
                show.Run.CoordinatorKey,
                show.Run.CreatedAtUtc,
                show.Run.SettledAtUtc,
            },
            tasks = show.Tasks.Select(t => new
            {
                id = t.Id,
                t.CliName,
                status = t.Status.ToString().ToLowerInvariant(),
                t.DependsOn,
                activeDispatchId = t.ActiveDispatchId,
            }),
            dispatches = show.OpenDispatches.Select(d => new
            {
                id = d.Id,
                taskId = d.TaskId,
                attempt = d.Attempt,
                state = d.State.ToString().ToLowerInvariant(),
                d.DispatchedAtUtc,
            }),
            unackedDeliveries = show.UnackedDeliveries.Select(d => new
            {
                id = d.Id,
                count = d.MessageIds.Count,
                d.CreatedAtUtc,
            }),
        });
    }
}
