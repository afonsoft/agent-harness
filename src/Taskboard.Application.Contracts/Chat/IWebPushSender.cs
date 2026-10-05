namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-009 seam: sends one Web Push
/// notification. The contract keeps the encryption/transport lib out of the
/// notifier (and out of tests).
/// </summary>
public interface IWebPushSender
{
    /// <summary>
    /// Result of a send attempt: <see cref="Sent"/>, or failure with an
    /// optional HTTP status so the caller can prune dead subscriptions
    /// (404/410 = gone for good, per RFC 8030 §7.3).
    /// </summary>
    /// <param name="HttpStatus">push-service status code on failure, when known.</param>
    public sealed record Result(bool Sent, int? HttpStatus = null, string? Error = null);

    /// <summary>Delivers <paramref name="payloadJson"/> to a subscription endpoint.</summary>
    Task<Result> SendAsync(
        string endpoint, string p256dh, string auth,
        string payloadJson, CancellationToken cancellationToken = default);
}
