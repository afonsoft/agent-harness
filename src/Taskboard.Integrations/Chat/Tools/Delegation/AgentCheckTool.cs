using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Dtos;
using Taskboard.Application.Contracts.Harness;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261020-v2 RF-003: durable mailbox check — returns the oldest
/// unacknowledged delivery (replayed until acked), or opens a new delivery
/// batch (≤50) over undelivered messages. <c>ack</c> commits a delivery and
/// returns the next; <c>peek</c>/<c>all</c> never consume.
/// </summary>
public sealed class AgentCheckTool(
    IServiceScopeFactory scopeFactory,
    PromptDefensePolicy? promptDefense = null) : IChatTool
{
    // SPEC-20261023 RF-B01: mailbox payloads are agent-written untrusted content.
    private readonly PromptDefensePolicy _defense = promptDefense ?? PromptDefensePolicy.Shared;
    public string Name => "agent_check";
    public string Description =>
        "Check mailbox deliveries for this conversation. Returns the current "
        + "unacknowledged delivery batch (replayed on every call until acked). "
        + "Pass ack=<deliveryId> to commit it and fetch the next batch; "
        + "peek=true previews without consuming; all=true lists deliveries; "
        + "wait=true polls up to timeout_ms for new mail.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "ack":{"type":"string","description":"Delivery id to acknowledge — commits it and returns the next batch"},
          "peek":{"type":"boolean","description":"Preview pending messages without opening a delivery"},
          "all":{"type":"boolean","description":"List all deliveries of the run (metadata only)"},
          "wait":{"type":"boolean","description":"Poll until a message arrives or timeout_ms elapses"},
          "timeout_ms":{"type":"integer","description":"Wait timeout in ms (default 30000, max 120000)"},
          "types":{"type":"array","items":{"type":"string"},"description":"Restrict to message kinds (e.g. worker_done, escalation)"},
          "to":{"type":"string","description":"Restrict to one recipient address"}
        }}
        """;

    public string CapabilityId => "agent:check";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var scope = DelegationToolSupport.ScopeOf(context);
        var ack = ReadString(arguments, "ack");
        var peek = ReadBool(arguments, "peek");
        var all = ReadBool(arguments, "all");
        var wait = ReadBool(arguments, "wait");
        var timeout = arguments.TryGetProperty("timeout_ms", out var t) && t.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromMilliseconds(Math.Clamp(t.GetInt32(), 100, 120_000))
            : (TimeSpan?)null;
        var types = ReadStringList(arguments, "types");

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();

        IReadOnlyCollection<string> recipients = ReadString(arguments, "to") is { } to
            ? [to]
            : await DelegationToolSupport.RecipientsAsync(service, context, scope, cancellationToken)
                .ConfigureAwait(false);

        try
        {
            var result = await service.CheckDeliveriesAsync(
                new CheckDeliveriesRequest(
                    scope, recipients, ack, peek, all,
                    Types: types.Count == 0 ? null : types,
                    Wait: wait, Timeout: timeout),
                cancellationToken).ConfigureAwait(false);

            return DelegationToolSupport.Ok(new
            {
                scope,
                delivery = result.Delivery is { } d
                    ? new { id = d.Id, count = d.MessageIds.Count, d.CreatedAtUtc, d.AckedAtUtc }
                    : null,
                messages = result.Messages.Select(m => new
                {
                    id = m.Id,
                    m.FromAgent,
                    m.ToAgent,
                    m.Kind,
                    Payload = _defense.Apply(m.Payload, "mailbox"),
                    dispatchId = m.DispatchId,
                    m.CreatedAt,
                }),
                pendingCount = result.PendingCount,
                timedOut = result.TimedOut,
                deliveries = result.Deliveries?.Select(d => new
                {
                    id = d.Id,
                    count = d.MessageIds.Count,
                    d.CreatedAtUtc,
                    d.AckedAtUtc,
                }),
            });
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "check refused");
        }
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static bool ReadBool(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

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
