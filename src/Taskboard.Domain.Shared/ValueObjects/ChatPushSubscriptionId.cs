namespace Taskboard.ValueObjects;

/// <summary>Identifier of a Web Push subscription (SPEC-20261005-chat-background-resume RF-009).</summary>
public sealed record ChatPushSubscriptionId : StringIdBase
{
    public ChatPushSubscriptionId(string value)
        : base(value)
    {
    }

    public static ChatPushSubscriptionId From(string value) => new(value);

    public static ChatPushSubscriptionId NewGuid() => new(Guid.NewGuid().ToString("N"));
}
