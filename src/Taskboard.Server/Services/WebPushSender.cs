using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Configuration;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Server.Services;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-009: <see cref="IWebPushSender"/>
/// over Lib.Net.Http.WebPush — VAPID-signed <c>RequestPushMessageDeliveryAsync</c>.
/// The VAPID identity comes from <see cref="VapidKeyService"/> (auto-generated
/// and persisted on first use).
/// </summary>
public sealed class WebPushSender(
    VapidKeyService vapidKeys,
    IHttpClientFactory httpClientFactory,
    ILogger<WebPushSender> logger) : IWebPushSender
{
    public async Task<IWebPushSender.Result> SendAsync(
        string endpoint, string p256dh, string auth,
        string payloadJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var keys = await vapidKeys.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
            using var vapid = new VapidAuthentication(keys.PublicKey, keys.PrivateKey)
            {
                Subject = keys.Subject,
            };
            var subscription = new PushSubscription { Endpoint = endpoint };
            subscription.SetKey(PushEncryptionKeyName.P256DH, p256dh);
            subscription.SetKey(PushEncryptionKeyName.Auth, auth);
            var client = new PushServiceClient(httpClientFactory.CreateClient("webpush"));
            await client.RequestPushMessageDeliveryAsync(
                    subscription, new PushMessage(payloadJson), vapid, cancellationToken)
                .ConfigureAwait(false);
            return new IWebPushSender.Result(Sent: true);
        }
        catch (PushServiceClientException ex)
        {
            return new IWebPushSender.Result(
                Sent: false, HttpStatus: (int)ex.StatusCode, Error: ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "web push send failed for {Endpoint}", Truncate(endpoint));
            return new IWebPushSender.Result(Sent: false, Error: ex.Message);
        }
    }

    private static string Truncate(string endpoint) =>
        endpoint.Length <= 60 ? endpoint : endpoint[..57] + "...";
}
