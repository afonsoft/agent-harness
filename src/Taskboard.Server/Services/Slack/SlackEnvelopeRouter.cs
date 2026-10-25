using System.Text.Json;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Server.Services.Slack;

/// <summary>One outcome of routing a Socket Mode envelope.</summary>
public abstract record SlackDispatch;

/// <summary>The envelope must be acked back over the socket.</summary>
public sealed record SlackAck(string EnvelopeId) : SlackDispatch;

/// <summary>A channel message destined for the chat bridge.</summary>
public sealed record SlackInbound(ChatChannelInboundMessage Message) : SlackDispatch;

/// <summary>An Approve/Reject button press on an approval card.</summary>
public sealed record SlackApprovalAction(string ApprovalId, string Outcome, string? Actor) : SlackDispatch;

/// <summary>Slack asked us to reconnect (or the socket dropped).</summary>
public sealed record SlackReconnect : SlackDispatch;

/// <summary>
/// SPEC-20261025 C1: pure Socket Mode envelope → dispatch table. Envelopes
/// get an <see cref="SlackAck"/> first (Slack retries un-acked envelopes);
/// <c>events_api</c> message events become <see cref="SlackInbound"/>;
/// <c>interactive</c> block_actions on <c>harness_approval:*</c> buttons
/// become <see cref="SlackApprovalAction"/>. Bot/subtype messages are
/// dropped — the card posts must never echo back as user input.
/// </summary>
public static class SlackEnvelopeRouter
{
    public const string ApprovalActionPrefix = "harness_approval:";

    public static IEnumerable<SlackDispatch> Route(JsonElement envelope)
    {
        if (envelope.TryGetProperty("envelope_id", out var idEl) && idEl.GetString() is { Length: > 0 } id)
        {
            yield return new SlackAck(id);
        }

        var type = envelope.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            case "hello":
                yield break;
            case "disconnect":
                yield return new SlackReconnect();
                yield break;
            case "events_api":
                foreach (var d in RouteEvent(envelope))
                {
                    yield return d;
                }
                yield break;
            case "interactive":
                foreach (var d in RouteInteractive(envelope))
                {
                    yield return d;
                }
                yield break;
            default:
                yield break;
        }
    }

    private static IEnumerable<SlackDispatch> RouteEvent(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("event", out var evt)
            || !evt.TryGetProperty("type", out var evtType)
            || evtType.GetString() is not "message")
        {
            yield break;
        }

        // Bot echoes, edits, joins — only human text counts.
        if (evt.TryGetProperty("bot_id", out _) || evt.TryGetProperty("subtype", out _))
        {
            yield break;
        }

        var text = evt.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;
        var channel = evt.TryGetProperty("channel", out var ch) ? ch.GetString() : null;
        var ts = evt.TryGetProperty("ts", out var tsEl) ? tsEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(ts))
        {
            yield break;
        }

        // Replies carry thread_ts (the root ts); roots bind by their own ts.
        var thread = evt.TryGetProperty("thread_ts", out var tt) && tt.GetString() is { Length: > 0 } t
            ? t
            : ts;
        var user = evt.TryGetProperty("user", out var u) ? u.GetString() : null;

        yield return new SlackInbound(new ChatChannelInboundMessage(
            SlackApiClient.ChannelName, channel, thread, user, user, text));
    }

    private static IEnumerable<SlackDispatch> RouteInteractive(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("actions", out var actions))
        {
            yield break;
        }

        var actor = payload.TryGetProperty("user", out var user)
            && user.TryGetProperty("username", out var un) && un.GetString() is { Length: > 0 } name
                ? $"@{name}"
                : payload.TryGetProperty("user", out var user2)
                    && user2.TryGetProperty("id", out var uid) ? uid.GetString() : null;

        foreach (var action in actions.EnumerateArray())
        {
            if (action.TryGetProperty("action_id", out var aid)
                && aid.GetString() is { } actionId
                && actionId.StartsWith(ApprovalActionPrefix, StringComparison.Ordinal)
                && action.TryGetProperty("value", out var v)
                && v.GetString() is "allow" or "deny")
            {
                yield return new SlackApprovalAction(
                    actionId[ApprovalActionPrefix.Length..], v.GetString()!, actor);
            }
        }
    }
}
