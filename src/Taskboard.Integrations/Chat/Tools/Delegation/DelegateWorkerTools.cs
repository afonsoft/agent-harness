using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>SPEC-20261020-v2 RF-006: dispatch inspection.</summary>
public sealed class DelegateWorkerShowTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_worker_show";
    public string Description =>
        "Show a worker dispatch — state, attempt, preamble, terminal handle and "
        + "timestamps.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "dispatch":{"type":"string","description":"Dispatch id (disp-*) or task id"}
        },"required":["dispatch"]}
        """;

    public string CapabilityId => "agent:worker_show";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var id = AgentAskTool.ReadString(arguments, "dispatch");
        if (string.IsNullOrWhiteSpace(id))
        {
            return DelegationToolSupport.Error("dispatch is required", "missing args");
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();

        var dispatch = await service.GetDispatchAsync(id, cancellationToken).ConfigureAwait(false);
        if (dispatch is null)
        {
            return DelegationToolSupport.Error($"unknown dispatch: {id}", "dispatch not found");
        }

        return DelegationToolSupport.Ok(new
        {
            dispatchId = dispatch.Id,
            dispatch.TaskId,
            dispatch.RunId,
            dispatch.WorkerKey,
            dispatch.Attempt,
            state = dispatch.State.ToString().ToLowerInvariant(),
            dispatch.TerminalHandle,
            hasOutput = dispatch.ArchivedOutput is not null,
            dispatch.CreatedAtUtc,
            dispatch.DispatchedAtUtc,
            dispatch.SettledAtUtc,
            dispatch.ReleasedAtUtc,
            preamble = TryParsePreamble(dispatch.PreambleJson),
        });
    }

    private static object? TryParsePreamble(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}

/// <summary>SPEC-20261020-v2 RF-006: archived worker output (last N lines).</summary>
public sealed class DelegateWorkerReadTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_worker_read";
    public string Description =>
        "Read the archived output of a released dispatch (delegate_worker_release "
        + "archives the worker transcript). limit truncates to the last N lines.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "dispatch":{"type":"string","description":"Dispatch id (disp-*)"},
          "limit":{"type":"integer","description":"Max lines from the end (default 200)"}
        },"required":["dispatch"]}
        """;

    public string CapabilityId => "agent:worker_read";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var id = AgentAskTool.ReadString(arguments, "dispatch");
        if (string.IsNullOrWhiteSpace(id))
        {
            return DelegationToolSupport.Error("dispatch is required", "missing args");
        }

        var limit = 200;
        if (arguments.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number)
        {
            limit = l.GetInt32();
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var output = await service.ReadDispatchOutputAsync(id, limit, cancellationToken)
            .ConfigureAwait(false);
        if (output is null)
        {
            return DelegationToolSupport.Error(
                $"no archived output for {id} — release the dispatch first with --output",
                "output not found");
        }

        return DelegationToolSupport.Ok(new { dispatchId = id, output });
    }
}

/// <summary>SPEC-20261020-v2 RF-006: settled|retained → released, archiving output.</summary>
public sealed class DelegateWorkerReleaseTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_worker_release";
    public string Description =>
        "Release a settled/retained worker dispatch — archives its output "
        + "(readable via delegate_worker_read) and clears the task's active attempt.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "dispatch":{"type":"string","description":"Dispatch id (disp-*)"},
          "output":{"type":"string","description":"Worker output to archive before release"}
        },"required":["dispatch"]}
        """;

    public string CapabilityId => "agent:worker_release";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var id = AgentAskTool.ReadString(arguments, "dispatch");
        if (string.IsNullOrWhiteSpace(id))
        {
            return DelegationToolSupport.Error("dispatch is required", "missing args");
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        try
        {
            var dispatch = await service.ReleaseDispatchAsync(
                DelegationToolSupport.ScopeOf(context), id,
                AgentAskTool.ReadString(arguments, "output"), cancellationToken)
                .ConfigureAwait(false);
            if (dispatch is null)
            {
                return DelegationToolSupport.Error($"unknown dispatch: {id}", "dispatch not found");
            }

            return DelegationToolSupport.Ok(new
            {
                dispatchId = dispatch.Id,
                dispatch.TaskId,
                state = dispatch.State.ToString().ToLowerInvariant(),
                hasOutput = dispatch.ArchivedOutput is not null,
            });
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "release refused");
        }
    }
}

/// <summary>SPEC-20261020-v2 RF-006: settled → retained (kept for follow-up).</summary>
public sealed class DelegateWorkerRetainTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_worker_retain";
    public string Description =>
        "Retain a settled worker dispatch — keeps the worker context alive for "
        + "a follow-up instead of releasing it.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "dispatch":{"type":"string","description":"Dispatch id (disp-*)"}
        },"required":["dispatch"]}
        """;

    public string CapabilityId => "agent:worker_retain";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
        await TransitionAsync(
            scopeFactory, arguments, context, cancellationToken,
            (s, scope, id, ct) => s.RetainDispatchAsync(scope, id, ct), "retain refused")
            .ConfigureAwait(false);

    internal static async Task<ChatToolResult> TransitionAsync(
        IServiceScopeFactory scopeFactory, JsonElement arguments, ChatToolContext context,
        CancellationToken ct,
        Func<IDelegationService, string, string, CancellationToken, Task<Taskboard.Dtos.DispatchDto?>> transition,
        string errorHint)
    {
        var id = AgentAskTool.ReadString(arguments, "dispatch");
        if (string.IsNullOrWhiteSpace(id))
        {
            return DelegationToolSupport.Error("dispatch is required", "missing args");
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        try
        {
            var dispatch = await transition(
                service, DelegationToolSupport.ScopeOf(context), id, ct).ConfigureAwait(false);
            if (dispatch is null)
            {
                return DelegationToolSupport.Error($"unknown dispatch: {id}", "dispatch not found");
            }

            return DelegationToolSupport.Ok(new
            {
                dispatchId = dispatch.Id,
                dispatch.TaskId,
                state = dispatch.State.ToString().ToLowerInvariant(),
            });
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, errorHint);
        }
    }
}

/// <summary>SPEC-20261020-v2 RF-006: non-released → abandoned (worker left mid-run).</summary>
public sealed class DelegateWorkerAbandonTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_worker_abandon";
    public string Description =>
        "Abandon a worker dispatch — marks the attempt abandoned and clears the "
        + "task's active dispatch so a retry can take over.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "dispatch":{"type":"string","description":"Dispatch id (disp-*)"}
        },"required":["dispatch"]}
        """;

    public string CapabilityId => "agent:worker_abandon";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
        DelegateWorkerRetainTool.TransitionAsync(
            scopeFactory, arguments, context, cancellationToken,
            (s, scope, id, ct) => s.AbandonDispatchAsync(scope, id, ct), "abandon refused");
}

/// <summary>
/// SPEC-20261020-v2 RF-010: clears run state — tasks (+dispatches+gates)
/// and/or messages (+deliveries+questions). Open dispatches require force.
/// </summary>
public sealed class DelegateResetTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_reset";
    public string Description =>
        "Reset the coordination run of this conversation — deletes tasks "
        + "(with dispatches and gates) and/or mailbox messages (with deliveries "
        + "and questions). Refuses while open dispatches exist unless force.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "tasks":{"type":"boolean","description":"Delete tasks, dispatches and gates"},
          "messages":{"type":"boolean","description":"Delete messages, deliveries and questions"},
          "all":{"type":"boolean","description":"Both tasks and messages"},
          "force":{"type":"boolean","description":"Allow reset with open dispatches"}
        }}
        """;

    public string CapabilityId => "agent:reset";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => true;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var tasks = ReadBool(arguments, "tasks") || ReadBool(arguments, "all");
        var messages = ReadBool(arguments, "messages") || ReadBool(arguments, "all");
        var force = ReadBool(arguments, "force");

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        try
        {
            var result = await service.ResetRunAsync(
                DelegationToolSupport.ScopeOf(context), tasks, messages, force, cancellationToken)
                .ConfigureAwait(false);
            return DelegationToolSupport.Ok(new
            {
                result.TasksDeleted,
                result.MessagesDeleted,
            });
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "reset refused");
        }
    }

    private static bool ReadBool(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
