using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261005 RF-008: reads the conversation mailbox — messages addressed
/// to <c>@all</c>/<c>@idle</c>, the picked CLI, the scope or any local task —
/// and marks them read by default.
/// </summary>
public sealed class AgentInboxTool(
    IServiceScopeFactory scopeFactory,
    PromptDefensePolicy? promptDefense = null) : IChatTool
{
    // SPEC-20261023 RF-B01: mailbox payloads are agent-written untrusted content.
    private readonly PromptDefensePolicy _defense = promptDefense ?? PromptDefensePolicy.Shared;
    public string Name => "agent_inbox";
    public string Description =>
        "[deprecated — prefer agent_check with durable deliveries] Read mailbox "
        + "messages sent to this conversation (worker_done/escalation "
        + "signals from delegated tasks, agent replies). Defaults to unread "
        + "messages and marks them read.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "unread_only":{"type":"boolean","description":"Only unread messages (default true)"},
          "mark_read":{"type":"boolean","description":"Mark returned messages read (default true)"},
          "to":{"type":"string","description":"Restrict to one recipient address"},
          "limit":{"type":"integer","description":"Max messages (default 50)"}
        }}
        """;

    public string CapabilityId => "agent:inbox";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var scope = DelegationToolSupport.ScopeOf(context);
        var unreadOnly = !IsDisabled(arguments, "unread_only");
        var markRead = !IsDisabled(arguments, "mark_read");
        var limit = arguments.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number
            ? Math.Clamp(l.GetInt32(), 1, 200)
            : 50;

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();

        IReadOnlyCollection<string> recipients = ReadString(arguments, "to") is { } to
            ? [to]
            : await DelegationToolSupport.RecipientsAsync(service, context, scope, cancellationToken)
                .ConfigureAwait(false);

        var messages = await service.ReadInboxAsync(
            scope, recipients, limit, unreadOnly, markRead, cancellationToken)
            .ConfigureAwait(false);

        return DelegationToolSupport.Ok(new
        {
            scope,
            messages = messages.Select(m => new
            {
                m.Id,
                m.FromAgent,
                m.ToAgent,
                m.Kind,
                Payload = _defense.Apply(m.Payload, "mailbox"),
                m.CreatedAt,
                m.ReadAt,
            }),
        });
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    /// <summary>True when the flag exists and is explicitly boolean-false.</summary>
    private static bool IsDisabled(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.False;
}
