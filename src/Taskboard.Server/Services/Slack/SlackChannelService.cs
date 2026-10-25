using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Server.Services.Slack;

/// <summary>
/// SPEC-20261025 C1: Socket Mode loop — <c>apps.connections.open</c> → WSS →
/// per-envelope ack + dispatch via <see cref="SlackEnvelopeRouter"/>. Runs
/// only while the adapter is enabled + tokened; a disabled or unconfigured
/// adapter idles on a slow poll so Settings edits take effect without
/// restart. Scoped services (config, bridge, repos) are resolved in
/// short-lived scopes — nothing scoped escapes into the socket lifetime.
/// Failures back off and reconnect; a <c>disconnect</c> envelope reconnects
/// immediately.
/// </summary>
public sealed class SlackChannelService(
    IServiceScopeFactory scopeFactory,
    ILogger<SlackChannelService> logger) : BackgroundService
{
    private static readonly TimeSpan IdlePoll = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReconnectBackoff = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            string? url;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var slack = scope.ServiceProvider.GetRequiredService<SlackApiClient>();
                if (!slack.IsReady)
                {
                    url = null;
                }
                else
                {
                    try
                    {
                        url = await slack.OpenSocketUrlAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "slack: apps.connections.open failed");
                        url = null;
                    }
                }

                if (url is null && !slack.IsReady)
                {
                    break; // signal: idle, not failure
                }

                if (url is not null)
                {
                    try
                    {
                        await PumpAsync(url, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "slack: socket loop failed — reconnecting");
                    }
                }
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(url is null ? IdlePoll : ReconnectBackoff, stoppingToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task PumpAsync(string url, CancellationToken stoppingToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url), stoppingToken).ConfigureAwait(false);
        logger.LogInformation("slack: socket mode connected");

        var buffer = new byte[64 * 1024];
        while (socket.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
        {
            using var message = await ReceiveMessageAsync(socket, buffer, stoppingToken).ConfigureAwait(false);
            if (message is null)
            {
                return; // closed by peer — reconnect
            }

            var reconnect = await DispatchAsync(socket, message.RootElement, stoppingToken).ConfigureAwait(false);
            if (reconnect)
            {
                return;
            }
        }
    }

    private static async Task<JsonDocument?> ReceiveMessageAsync(
        ClientWebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            stream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        try
        {
            return JsonDocument.Parse(stream.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<bool> DispatchAsync(
        ClientWebSocket socket, JsonElement envelope, CancellationToken ct)
    {
        foreach (var dispatch in SlackEnvelopeRouter.Route(envelope))
        {
            switch (dispatch)
            {
                case SlackAck ack:
                    var payload = JsonSerializer.SerializeToUtf8Bytes(new { envelope_id = ack.EnvelopeId });
                    await socket.SendAsync(payload, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                    break;
                case SlackReconnect:
                    return true;
                case SlackInbound inbound:
                    await SafeScopeAsync(
                        bridge => bridge.InboundMessageAsync(inbound.Message, ct),
                        "inbound", ct).ConfigureAwait(false);
                    break;
                case SlackApprovalAction action:
                    await SafeScopeAsync(
                        bridge => bridge.DecideApprovalAsync(
                            action.ApprovalId, action.Outcome, SlackApiClient.ChannelName,
                            action.Actor, reason: null, ct),
                        "approval", ct).ConfigureAwait(false);
                    break;
            }
        }
        return false;
    }

    /// <summary>Bridge calls get a fresh scope — scoped repos must not escape the hosted service.</summary>
    private async Task SafeScopeAsync(
        Func<IChatChannelBridge, Task> call, string what, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var bridge = scope.ServiceProvider.GetRequiredService<IChatChannelBridge>();
            await call(bridge).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "slack: {What} dispatch failed", what);
        }
    }
}
