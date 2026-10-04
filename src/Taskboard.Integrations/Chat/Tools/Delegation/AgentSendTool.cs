using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261005 RF-008: posts a message into the conversation mailbox —
/// the channel agents and delegated workers use to talk. System kinds
/// (<c>worker_done</c>/<c>heartbeat</c>/<c>escalation</c>/<c>decision</c>) are
/// dispatcher-only.
/// </summary>
public sealed class AgentSendTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "agent_send";
    public string Description =>
        "Send a mailbox message to an agent or broadcast (@all/@idle) inside "
        + "this conversation — coordinate with delegated tasks. Read replies "
        + "with agent_inbox.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "to":{"type":"string","description":"Recipient: @all, @idle, a cli/def name, a task id or the conversation scope"},
          "text":{"type":"string","description":"Message body"}
        },"required":["to","text"]}
        """;

    public string CapabilityId => "agent:send";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var to = ReadString(arguments, "to");
        var text = ReadString(arguments, "text");
        if (string.IsNullOrWhiteSpace(to))
        {
            return DelegationToolSupport.Error("to is required", "missing recipient");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return DelegationToolSupport.Error("text is required", "empty message");
        }

        var kind = ReadString(arguments, "kind");
        if (kind is not null && !AgentMailboxKinds.AgentWritable.Contains(kind))
        {
            return DelegationToolSupport.Error(
                $"kind '{kind}' is system-only; use 'text'", "reserved kind");
        }

        var scope = DelegationToolSupport.ScopeOf(context);
        var from = string.IsNullOrWhiteSpace(context.DefaultAgentCli)
            ? "assistant"
            : context.DefaultAgentCli!;

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var message = await service.PostAsync(
            new PostMailboxMessageRequest(scope, from, to, text, kind ?? AgentMailboxKinds.Text),
            cancellationToken).ConfigureAwait(false);

        return DelegationToolSupport.Ok(new { message.Id, message.ToAgent, message.Kind });
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
