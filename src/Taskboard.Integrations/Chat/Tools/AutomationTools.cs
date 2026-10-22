using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Dtos;
using Taskboard.Integrations.Chat.Tools.Delegation;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// SPEC-20261021-agent-automations RF-007: automation tools — scheduled
/// prompts aimed at an agent CLI on a repo/worktree. The service is scoped
/// (EF), so the singleton tool resolves it per call.
/// </summary>
public sealed class AutomationCreateTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "automation_create";
    public string Description =>
        "Create a durable scheduled prompt for an agent CLI. schedule accepts "
        + "a 5-field cron, an ISO-8601 UTC instant, or a preset "
        + "(hourly|daily|weekdays|weekly). target: repo:<name>|worktree:<path>|project:<id>. "
        + "reuseSession rebinds the last succeeded run's worktree session; "
        + "extraAgentArgs is whitelisted (--model/--effort/--add-dir only).";
    public string ParametersJson => """
        {"type":"object","properties":{
          "name":{"type":"string","description":"<=120 chars"},
          "prompt":{"type":"string","description":"<=4000 chars — delivered when the schedule fires"},
          "cli":{"type":"string","description":"agent CLI kind (claude|codex|…) or custom def id"},
          "target":{"type":"string","description":"repo:<name> | worktree:<path> | project:<id>"},
          "schedule":{"type":"string","description":"5-field cron | ISO-8601 instant | hourly|daily|weekdays|weekly"},
          "timezone":{"type":"string","description":"IANA timezone for cron (default UTC)"},
          "missedRunGraceMinutes":{"type":"integer","description":"0–1440, default 60"},
          "reuseSession":{"type":"boolean"},
          "extraAgentArgs":{"type":"string","description":"whitelisted: --model/--effort/--add-dir only"},
          "precheck":{"type":"string","description":"optional shell gate (exit!=0 skips the fire)"}}
        ,"required":["name","prompt","cli","target","schedule"]}
        """;

    public string CapabilityId => "agent:automation";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => true;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var name = DelegationToolSupport.ReadString(arguments, "name");
        var prompt = DelegationToolSupport.ReadString(arguments, "prompt");
        var cli = DelegationToolSupport.ReadString(arguments, "cli");
        var target = DelegationToolSupport.ReadString(arguments, "target");
        var schedule = DelegationToolSupport.ReadString(arguments, "schedule");
        if (name is null || prompt is null || cli is null || target is null || schedule is null)
        {
            return DelegationToolSupport.Error(
                "name, prompt, cli, target and schedule are required", "missing args");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAutomationService>();
        try
        {
            var dto = await service.CreateAsync(
                new CreateAutomationRequest(
                    name, prompt, cli, target, schedule,
                    TimeZoneId: DelegationToolSupport.ReadString(arguments, "timezone"),
                    MissedRunGraceMinutes: ReadInt(arguments, "missedRunGraceMinutes"),
                    ReuseSession: ReadBool(arguments, "reuseSession"),
                    ExtraAgentArgs: DelegationToolSupport.ReadString(arguments, "extraAgentArgs"),
                    Precheck: DelegationToolSupport.ReadString(arguments, "precheck")),
                cancellationToken).ConfigureAwait(false);
            return DelegationToolSupport.Ok(new
            {
                dto.Id,
                dto.Name,
                dto.ScheduleKind,
                dto.CronExpression,
                dto.NextFireAtUtc
            });
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, ex.Code);
        }
    }

    internal static int? ReadInt(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.TryGetInt32(out var iv) ? iv : null;

    internal static bool ReadBool(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v)
            && v.ValueKind is JsonValueKind.True;
}

/// <summary>SPEC-20261021-agent-automations RF-007: list automations.</summary>
public sealed class AutomationListTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "automation_list";
    public string Description =>
        "List scheduled agent automations (name, cli, target, schedule, next fire, enabled).";
    public string ParametersJson => """{"type":"object","properties":{}}""";

    public string CapabilityId => "agent:automation";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAutomationService>();
        var rows = await service.ListAsync(cancellationToken).ConfigureAwait(false);
        return DelegationToolSupport.Ok(new
        {
            automations = rows.Select(a => new
            {
                a.Id,
                a.Name,
                a.AgentCli,
                a.Target,
                a.ScheduleKind,
                a.CronExpression,
                a.NextFireAtUtc,
                a.Enabled
            })
        });
    }
}

/// <summary>SPEC-20261021-agent-automations RF-007: force-fire an automation now.</summary>
public sealed class AutomationRunTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "automation_run";
    public string Description =>
        "Fire an automation immediately (precheck still applies). Returns the run row.";
    public string ParametersJson => """
        {"type":"object","properties":{"id":{"type":"string"}},"required":["id"]}
        """;

    public string CapabilityId => "agent:automation";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => true;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var id = DelegationToolSupport.ReadString(arguments, "id");
        if (id is null)
        {
            return DelegationToolSupport.Error("id is required", "missing args");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAutomationService>();
        var run = await service.RunNowAsync(id, cancellationToken).ConfigureAwait(false);
        return run is null
            ? DelegationToolSupport.Error($"automation '{id}' not found", "not found")
            : DelegationToolSupport.Ok(new
            {
                run.Id,
                run.Outcome,
                run.TaskId,
                run.SessionId,
                run.SkipReason
            });
    }
}
