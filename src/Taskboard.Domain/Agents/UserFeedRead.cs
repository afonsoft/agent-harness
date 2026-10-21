namespace Taskboard.Domain.Agents;

/// <summary>
/// SPEC-20261020-agents-feed-notifications RF-004: per-user read marker on a
/// feed item. Unread = no row OR <see cref="ManuallyUnread"/>; read-all deletes
/// rows (clearing ManuallyUnread too).
/// </summary>
public sealed class UserFeedRead : Entity<string>
{
    private UserFeedRead(string id)
        : base(id)
    {
    }

    public string UserId { get; private set; } = string.Empty;
    public string ItemId { get; private set; } = string.Empty;
    public DateTime ReadAtUtc { get; private set; }
    public bool ManuallyUnread { get; private set; }

    public static string KeyOf(string userId, string itemId) => $"{userId}|{itemId}";

    public static UserFeedRead Create(string userId, string itemId, DateTime atUtc) =>
        new(KeyOf(userId, itemId))
        {
            UserId = userId,
            ItemId = itemId,
            ReadAtUtc = atUtc,
        };

    public void Touch(DateTime atUtc)
    {
        ReadAtUtc = atUtc;
        ManuallyUnread = false;
    }

    public void MarkUnread() => ManuallyUnread = true;
}
