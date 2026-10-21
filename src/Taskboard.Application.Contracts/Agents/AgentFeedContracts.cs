using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>SPEC-20261020-agents-feed-notifications RF-001: wire shape of a feed item.</summary>
public sealed record AgentFeedItemDto(
    string Id,
    AgentFeedKind Kind,
    string Title,
    string? Body,
    string DeepLink,
    string? SessionKey,
    string? Cli,
    int? IssueNumber,
    DateTime AtUtc,
    bool NeedsAttention,
    int RepeatCount,
    bool Unread);

/// <summary>SPEC RF-003: one page of the feed + opaque cursor for the next page.</summary>
public sealed record FeedPageDto(
    IReadOnlyList<AgentFeedItemDto> Items,
    string? NextCursor);

/// <summary>SPEC RF-003: badge counts — total unread vs attention-only.</summary>
public sealed record FeedUnreadCountDto(int Total, int Attention);

/// <summary>Producer input for <see cref="IAgentFeedService.ProduceAsync"/>.</summary>
public sealed record FeedItemRequest(
    AgentFeedKind Kind,
    string Title,
    string? Body,
    string DeepLink,
    string? SessionKey,
    string? Cli,
    int? IssueNumber,
    bool NeedsAttention,
    string UserId,
    DateTime? AtUtc = null);

/// <summary>SPEC RF-001/RF-003/RF-008: persistence of feed items.</summary>
public interface IAgentFeedRepository
{
    Task<AgentFeedItemDto> AddAsync(FeedItemRequest request, DateTime atUtc, CancellationToken ct = default);

    Task<AgentFeedItemDto?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>Newest-first page: items with (AtUtc,Id) strictly before the cursor.</summary>
    Task<IReadOnlyList<AgentFeedItemDto>> ListPageAsync(
        string userId, DateTime? beforeUtc, string? beforeId, int take, CancellationToken ct = default);

    /// <summary>RF-008: dedup lookup — same (kind, sessionKey, deepLink) inside the merge window.</summary>
    Task<AgentFeedItemDto?> FindDedupAsync(
        string userId, AgentFeedKind kind, string? sessionKey, string deepLink,
        DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>RF-008: bump RepeatCount + AtUtc on the merged item.</summary>
    Task<AgentFeedItemDto?> MergeRepeatAsync(string id, DateTime atUtc, CancellationToken ct = default);

    /// <summary>Unread = no UserFeedRead row OR ManuallyUnread — filtered by needsAttention.</summary>
    Task<int> CountUnreadAsync(string userId, bool needsAttentionOnly, CancellationToken ct = default);

    /// <summary>Read flags for a batch of items: itemId → manuallyUnread.</summary>
    Task<IReadOnlyDictionary<string, bool>> UnreadFlagsAsync(
        string userId, IReadOnlyCollection<string> itemIds, CancellationToken ct = default);

    /// <summary>RF-009: retention — deletes items older than the cutoff (and their read rows).</summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);
}

/// <summary>SPEC RF-004: per-user read markers on feed items.</summary>
public interface IUserFeedReadRepository
{
    /// <summary>Idempotent: creates or touches the marker, clears manuallyUnread.</summary>
    Task MarkReadAsync(string userId, string itemId, DateTime atUtc, CancellationToken ct = default);

    /// <summary>Idempotent: sets manuallyUnread=true on a row (created if absent).</summary>
    Task MarkUnreadAsync(string userId, string itemId, DateTime atUtc, CancellationToken ct = default);

    /// <summary>Read-all: deletes every marker of the user (clears manuallyUnread too).</summary>
    Task<int> DeleteAllForUserAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// SPEC RF-010: SignalR <c>agent.feed</c> push payload — a new/merged
/// <paramref name="Item"/>, a read-state <paramref name="Kind"/>="patch", plus
/// the user's current unread counts either way.
/// </summary>
public sealed record AgentFeedPushDto(
    string Kind,
    AgentFeedItemDto? Item,
    FeedUnreadCountDto UnreadCounts);

/// <summary>SPEC RF-006: one category's {badge, sound, persist} toggles.</summary>
public sealed record FeedCategoryPreference(bool Badge, bool Sound, bool Persist);

/// <summary>
/// SPEC RF-006: full preference map for a user — every
/// <see cref="AgentFeedKind"/> present (defaults merged over stored rows).
/// </summary>
public sealed record AgentFeedPreferencesDto(
    IReadOnlyDictionary<AgentFeedKind, FeedCategoryPreference> Categories);

/// <summary>SPEC RF-006: persistence of per-user per-category overrides.</summary>
public interface IUserFeedPreferenceRepository
{
    Task<IReadOnlyDictionary<AgentFeedKind, FeedCategoryPreference>> GetForUserAsync(
        string userId, CancellationToken ct = default);

    Task UpsertAsync(
        string userId, AgentFeedKind kind, FeedCategoryPreference pref,
        CancellationToken ct = default);
}

/// <summary>
/// SPEC RF-006: merged preference resolution — stored overrides over
/// <see cref="AgentFeedDefaults"/>; results cached per user and invalidated
/// on write.
/// </summary>
public interface IAgentFeedPreferenceService
{
    /// <summary>Effective preference for one (user, kind).</summary>
    Task<FeedCategoryPreference> ResolveAsync(
        string userId, AgentFeedKind kind, CancellationToken ct = default);

    /// <summary>Full merged map — defaults for categories without a row.</summary>
    Task<AgentFeedPreferencesDto> GetForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>Upserts the given categories; others keep their stored/default value.</summary>
    Task<AgentFeedPreferencesDto> SaveForUserAsync(
        string userId, IReadOnlyDictionary<AgentFeedKind, FeedCategoryPreference> categories,
        CancellationToken ct = default);
}

/// <summary>
/// SPEC RF-006: spec defaults — attention/question badge+sound; completion/
/// failure badge only; worktree/pr persist only. Everything persists.
/// </summary>
public static class AgentFeedDefaults
{
    public static FeedCategoryPreference For(AgentFeedKind kind) => kind switch
    {
        AgentFeedKind.Attention or AgentFeedKind.Question => new(true, true, true),
        AgentFeedKind.Completion or AgentFeedKind.Failure => new(true, false, true),
        _ => new(false, false, true),
    };
}

/// <summary>
/// SPEC RF-010: broadcast channel for feed updates. Implemented server-side
/// via SignalR; the service calls it fire-and-log so a push failure never
/// breaks the write it follows.
/// </summary>
public interface IAgentFeedNotifier
{
    /// <summary>Group name of a user's feed — <c>feed:{userId}</c>.</summary>
    static string GroupFor(string userId) => $"feed:{userId}";

    Task PushAsync(string userId, AgentFeedPushDto push, CancellationToken ct = default);
}

/// <summary>
/// SPEC RF-001…RF-005: produce feed items (dedup merge), page, unread counts
/// and read-state ops. Writes never block the producing transition.
/// </summary>
public interface IAgentFeedService
{
    Task<AgentFeedItemDto?> ProduceAsync(FeedItemRequest request, CancellationToken ct = default);

    Task<FeedPageDto> GetPageAsync(
        string userId, string? beforeCursor = null, int limit = 50, CancellationToken ct = default);

    Task<FeedUnreadCountDto> GetUnreadCountAsync(string userId, CancellationToken ct = default);

    Task MarkReadAsync(string userId, string itemId, CancellationToken ct = default);

    Task MarkUnreadAsync(string userId, string itemId, CancellationToken ct = default);

    Task<int> MarkAllReadAsync(string userId, CancellationToken ct = default);
}
