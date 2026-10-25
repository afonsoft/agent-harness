using System.Net.Http.Json;
using System.Text.Json;
using Taskboard.Application.Configuration;

namespace Taskboard.Server.Services.Slack;

/// <summary>
/// SPEC-20261025 C1: Slack Web API client — <c>chat.postMessage</c> for
/// approval cards/replies and <c>apps.connections.open</c> for the Socket
/// Mode WSS URL. Tokens resolve per call from the runtime configuration so
/// Settings edits need no restart. Config keys:
/// <c>Taskboard:Channels:Slack:Enabled|BotToken|AppToken|Model</c>.
/// </summary>
public sealed class SlackApiClient(
    IHttpClientFactory httpClientFactory,
    RuntimeConfigurationService configuration)
{
    public const string EnabledKey = "Taskboard:Channels:Slack:Enabled";
    public const string BotTokenKey = "Taskboard:Channels:Slack:BotToken";
    public const string AppTokenKey = "Taskboard:Channels:Slack:AppToken";
    public const string ModelKey = "Taskboard:Channels:Slack:Model";
    public const string ChannelName = "slack";

    private static readonly Uri ApiBase = new("https://slack.com/api/");

    /// <summary>True when the adapter is enabled and both tokens look right.</summary>
    public bool IsReady =>
        configuration.GetEffectiveBool(EnabledKey)
        && (configuration.GetEffectiveValue(BotTokenKey) ?? string.Empty).StartsWith("xoxb-", StringComparison.Ordinal)
        && (configuration.GetEffectiveValue(AppTokenKey) ?? string.Empty).StartsWith("xapp-", StringComparison.Ordinal);

    public string BotToken => configuration.GetEffectiveValue(BotTokenKey) ?? string.Empty;

    /// <summary>Socket Mode handshake → short-lived WSS URL; null on any failure.</summary>
    public async Task<string?> OpenSocketUrlAsync(CancellationToken cancellationToken)
    {
        var appToken = configuration.GetEffectiveValue(AppTokenKey) ?? string.Empty;
        var client = httpClientFactory.CreateClient("slack");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ApiBase, "apps.connections.open"))
        {
            Content = new FormUrlEncodedContent([]),
        };
        request.Headers.Authorization = new("Bearer", appToken);

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean()
            && doc.RootElement.TryGetProperty("url", out var url)
                ? url.GetString()
                : null;
    }

    /// <summary>Posts a message (blocks JSON optional) into a channel/thread. Best-effort — returns false on Slack errors.</summary>
    public async Task<bool> PostMessageAsync(
        string channel, string? threadTs, string text, JsonElement? blocks,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("slack");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ApiBase, "chat.postMessage"));
        request.Headers.Authorization = new("Bearer", BotToken);

        var payload = new Dictionary<string, object?>
        {
            ["channel"] = channel,
            ["text"] = text,
        };
        if (!string.IsNullOrEmpty(threadTs))
        {
            payload["thread_ts"] = threadTs;
        }
        if (blocks is { } b)
        {
            payload["blocks"] = b;
        }

        request.Content = JsonContent.Create(payload);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
}
