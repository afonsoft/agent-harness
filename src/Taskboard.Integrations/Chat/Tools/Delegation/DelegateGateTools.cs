using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261020-v2 RF-005: bind a gate to a task — the task is held
/// <c>blocked</c> until <see cref="DelegateGateResolveTool"/> resolves it.
/// </summary>
public sealed class DelegateGateCreateTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_gate_create";
    public string Description =>
        "Attach a blocking gate to a delegated task: the task stays 'blocked' "
        + "until delegate_gate_resolve answers it. Options list is optional.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "task_id":{"type":"string","description":"Task to gate"},
          "question":{"type":"string","description":"What must be decided"},
          "options":{"type":"array","items":{"type":"string"},"description":"Allowed resolutions"}
        },"required":["task_id","question"]}
        """;

    public string CapabilityId => "agent:gate_create";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var taskId = AgentAskTool.ReadString(arguments, "task_id");
        var question = AgentAskTool.ReadString(arguments, "question");
        if (string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(question))
        {
            return DelegationToolSupport.Error("task_id and question are required", "missing args");
        }

        var options = new List<string>();
        if (arguments.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array)
        {
            options.AddRange(o.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!));
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();

        try
        {
            var gate = await service.CreateGateAsync(
                DelegationToolSupport.ScopeOf(context), taskId, question,
                options.Count == 0 ? null : options, cancellationToken).ConfigureAwait(false);
            return DelegationToolSupport.Ok(new
            {
                gateId = gate.Id,
                gate.TaskId,
                status = gate.Status.ToString().ToLowerInvariant(),
            });
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "gate refused");
        }
    }
}

/// <summary>SPEC-20261020-v2 RF-005: resolve a gate — releases the task to the DAG verdict.</summary>
public sealed class DelegateGateResolveTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_gate_resolve";
    public string Description =>
        "Resolve an open gate: delegate_gate_resolve <gate_id> <resolution>. "
        + "The gated task unblocks and re-enters dependency evaluation.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "gate_id":{"type":"string","description":"Gate to resolve"},
          "resolution":{"type":"string","description":"Chosen resolution"}
        },"required":["gate_id","resolution"]}
        """;

    public string CapabilityId => "agent:gate_resolve";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var gateId = AgentAskTool.ReadString(arguments, "gate_id");
        var resolution = AgentAskTool.ReadString(arguments, "resolution");
        if (string.IsNullOrWhiteSpace(gateId) || string.IsNullOrWhiteSpace(resolution))
        {
            return DelegationToolSupport.Error("gate_id and resolution are required", "missing args");
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();

        try
        {
            var gate = await service.ResolveGateAsync(
                DelegationToolSupport.ScopeOf(context), gateId, resolution, cancellationToken)
                .ConfigureAwait(false);
            return DelegationToolSupport.Ok(new
            {
                gateId = gate.Id,
                gate.TaskId,
                status = gate.Status.ToString().ToLowerInvariant(),
                gate.Resolution,
            });
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "resolve refused");
        }
    }
}

/// <summary>SPEC-20261020-v2 RF-005: list gates of the scope (optionally task-bound).</summary>
public sealed class DelegateGateListTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_gate_list";
    public string Description =>
        "List task-bound gates of this conversation — open ones hold their "
        + "task blocked. Filter to a single task with task_id.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "task_id":{"type":"string","description":"List only gates of this task"}
        }}
        """;

    public string CapabilityId => "agent:gate_list";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var gates = await service.ListGatesAsync(
            DelegationToolSupport.ScopeOf(context),
            AgentAskTool.ReadString(arguments, "task_id"), cancellationToken).ConfigureAwait(false);

        return DelegationToolSupport.Ok(new
        {
            gates = gates.Select(g => new
            {
                id = g.Id,
                taskId = g.TaskId,
                g.Question,
                status = g.Status.ToString().ToLowerInvariant(),
                g.Resolution,
                g.CreatedAtUtc,
                g.ResolvedAtUtc,
            }),
        });
    }
}

/// <summary>
/// SPEC-20261020-v2 RF-010: external status update — for now only
/// <c>blocked</c> with an optional structured json reason.
/// </summary>
public sealed class DelegateTaskUpdateTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "delegate_task_update";
    public string Description =>
        "Update a delegated task externally — currently only status=blocked "
        + "with an optional structured json result (e.g. quota/lock reason).";
    public string ParametersJson => """
        {"type":"object","properties":{
          "id":{"type":"string","description":"Task id"},
          "status":{"type":"string","description":"New status (only 'blocked' supported)"},
          "result":{"type":"string","description":"Structured json reason"}
        },"required":["id","status"]}
        """;

    public string CapabilityId => "agent:task_update";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var taskId = AgentAskTool.ReadString(arguments, "id");
        var status = AgentAskTool.ReadString(arguments, "status");
        if (string.IsNullOrWhiteSpace(taskId)
            || !string.Equals(status, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return DelegationToolSupport.Error(
                "id is required and only status=blocked is supported", "unsupported update");
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var task = await service.BlockTaskAsync(
            DelegationToolSupport.ScopeOf(context), taskId,
            AgentAskTool.ReadString(arguments, "result"), cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return DelegationToolSupport.Error($"unknown task: {taskId}", "task not found");
        }

        return DelegationToolSupport.Ok(new
        {
            taskId = task.Id,
            status = task.Status.ToString().ToLowerInvariant(),
            task.ResultSummary,
        });
    }
}
