using Taskboard.ValueObjects;

namespace Taskboard.Domain.Entities.Chat;

/// <summary>
/// One browser's Web Push subscription (SPEC-20261005-chat-background-resume
/// RF-009): the push-service endpoint plus its auth secrets, written by
/// <c>taskboardNotify.subscribePush</c> when the user opts into browser-closed
/// delivery. <see cref="Endpoint"/> is unique — re-subscribing from the same
/// browser just refreshes the key material (<see cref="RotateKeys"/>).
/// </summary>
public sealed class ChatPushSubscription : AggregateRoot<ChatPushSubscriptionId>
{
    /// <summary>Push-service endpoint URL (FCM/Mozilla/Edge) — unique.</summary>
    public string Endpoint { get; private set; } = default!;

    /// <summary>Client ECDH public key (base64url) used to encrypt payloads.</summary>
    public string P256dh { get; private set; } = default!;

    /// <summary>Client auth secret (base64url) used to encrypt payloads.</summary>
    public string Auth { get; private set; } = default!;

    /// <summary>Browser hint for ops/debug — never required.</summary>
    public string? UserAgent { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    private ChatPushSubscription()
    {
    }

    private ChatPushSubscription(
        ChatPushSubscriptionId id, string endpoint, string p256dh, string auth,
        string? userAgent, DateTime now)
        : base(id)
    {
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        UserAgent = userAgent;
        CreatedAt = UpdatedAt = now;
    }

    public static ChatPushSubscription Create(
        ChatPushSubscriptionId id, string endpoint, string p256dh, string auth,
        string? userAgent = null, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Push endpoint cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Push subscription keys cannot be empty.");
        }

        var at = now ?? DateTime.UtcNow;
        return new ChatPushSubscription(id, endpoint.Trim(), p256dh.Trim(), auth.Trim(), userAgent, at);
    }

    /// <summary>Same endpoint, new key material (re-subscribe refresh).</summary>
    public void RotateKeys(string p256dh, string auth, string? userAgent, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth))
        {
            throw new DomainException(TaskboardDomainErrorCodes.InvalidValue, "Push subscription keys cannot be empty.");
        }

        P256dh = p256dh.Trim();
        Auth = auth.Trim();
        UserAgent = userAgent;
        UpdatedAt = now ?? DateTime.UtcNow;
    }
}
