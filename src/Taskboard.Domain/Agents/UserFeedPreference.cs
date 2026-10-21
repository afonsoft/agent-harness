using Taskboard.Agents;

namespace Taskboard.Domain.Agents;

/// <summary>
/// SPEC-20261020-agents-feed-notifications RF-006: per-user per-category
/// notification preference — one row per (user, kind) override; absent rows
/// fall back to <c>AgentFeedDefaults</c>.
/// </summary>
public sealed class UserFeedPreference : Entity<string>
{
    private UserFeedPreference(string id)
        : base(id)
    {
    }

    public string UserId { get; private set; } = string.Empty;
    public AgentFeedKind Kind { get; private set; }
    public bool Badge { get; private set; } = true;
    public bool Sound { get; private set; }
    public bool Persist { get; private set; } = true;

    public static string KeyOf(string userId, AgentFeedKind kind) => $"{userId}|{kind}";

    public static UserFeedPreference Create(
        string userId, AgentFeedKind kind, bool badge, bool sound, bool persist) =>
        new(KeyOf(userId, kind))
        {
            UserId = userId,
            Kind = kind,
            Badge = badge,
            Sound = sound,
            Persist = persist,
        };

    public void Update(bool badge, bool sound, bool persist)
    {
        Badge = badge;
        Sound = sound;
        Persist = persist;
    }
}
