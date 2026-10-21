using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261020-agents-feed-notifications: feed produce (RF-002, RF-008 dedup
/// merge), paging (RF-003), unread model (RF-004) and badge counts (RF-005).
/// Scoped service — producers call it fire-and-log so a feed write never
/// blocks the transition that produced it (NFR).
/// </summary>
public sealed class AgentFeedService : IAgentFeedService
{
    private const int MaxPageSize = 100;
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(5);

    private readonly IAgentFeedRepository _items;
    private readonly IUserFeedReadRepository _reads;
    private readonly IAgentFeedNotifier? _notifier;
    private readonly TimeProvider _time;
    private readonly ILogger<AgentFeedService>? _logger;

    public AgentFeedService(
        IAgentFeedRepository items, IUserFeedReadRepository reads,
        IAgentFeedNotifier? notifier = null,
        TimeProvider? timeProvider = null, ILogger<AgentFeedService>? logger = null)
    {
        _items = items;
        _reads = reads;
        _notifier = notifier;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<AgentFeedItemDto?> ProduceAsync(
        FeedItemRequest request, CancellationToken ct = default)
    {
        var atUtc = request.AtUtc ?? _time.GetUtcNow().UtcDateTime;

        // RF-008: dedup window — same (kind, sessionKey, deepLink) merges.
        var dedup = await _items.FindDedupAsync(
            request.UserId, request.Kind, request.SessionKey, request.DeepLink,
            atUtc - DedupWindow, ct).ConfigureAwait(false);

        var item = dedup is not null
            ? await _items.MergeRepeatAsync(dedup.Id, atUtc, ct).ConfigureAwait(false)
            : await _items.AddAsync(request, atUtc, ct).ConfigureAwait(false);

        // RF-010: live push — fire-and-log, never blocks the write.
        if (item is not null)
        {
            await PushSafeAsync(
                request.UserId,
                counts => Task.FromResult(new AgentFeedPushDto("item", item, counts)),
                ct).ConfigureAwait(false);
        }

        return item;
    }

    public async Task<FeedPageDto> GetPageAsync(
        string userId, string? beforeCursor = null, int limit = 50,
        CancellationToken ct = default)
    {
        var take = Math.Clamp(limit, 1, MaxPageSize);
        var (beforeUtc, beforeId) = ParseCursor(beforeCursor);
        var items = await _items.ListPageAsync(
            userId, beforeUtc, beforeId, take, ct).ConfigureAwait(false);

        string? next = items.Count == take
            ? $"{items[^1].AtUtc:o}|{items[^1].Id}"
            : null;
        return new FeedPageDto(items, next);
    }

    public async Task<FeedUnreadCountDto> GetUnreadCountAsync(
        string userId, CancellationToken ct = default)
    {
        var total = await _items.CountUnreadAsync(userId, false, ct).ConfigureAwait(false);
        var attention = await _items.CountUnreadAsync(userId, true, ct).ConfigureAwait(false);
        return new FeedUnreadCountDto(total, attention);
    }

    public async Task MarkReadAsync(string userId, string itemId, CancellationToken ct = default)
    {
        await _reads.MarkReadAsync(
            userId, itemId, _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        await PushPatchAsync(userId, ct).ConfigureAwait(false);
    }

    public async Task MarkUnreadAsync(string userId, string itemId, CancellationToken ct = default)
    {
        await _reads.MarkUnreadAsync(
            userId, itemId, _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        await PushPatchAsync(userId, ct).ConfigureAwait(false);
    }

    public async Task<int> MarkAllReadAsync(string userId, CancellationToken ct = default)
    {
        var cleared = await _reads.DeleteAllForUserAsync(userId, ct).ConfigureAwait(false);
        await PushPatchAsync(userId, ct).ConfigureAwait(false);
        return cleared;
    }

    // ---- RF-010: live push ----

    /// <summary>Push kind="patch" with the user's current unread counts.</summary>
    private Task PushPatchAsync(string userId, CancellationToken ct) =>
        PushSafeAsync(
            userId,
            counts => Task.FromResult(new AgentFeedPushDto("patch", null, counts)),
            ct);

    /// <summary>
    /// Builds the push (fetching fresh unread counts) and delivers it through
    /// the notifier when one is registered — errors are swallowed: the write
    /// it follows already succeeded and clients resync on reconnect.
    /// </summary>
    private async Task PushSafeAsync(
        string userId,
        Func<FeedUnreadCountDto, Task<AgentFeedPushDto>> build,
        CancellationToken ct)
    {
        if (_notifier is null)
        {
            return;
        }

        try
        {
            var counts = await GetUnreadCountAsync(userId, ct).ConfigureAwait(false);
            await _notifier.PushAsync(
                userId, await build(counts).ConfigureAwait(false), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Agent feed push failed for user {UserId}.", userId);
        }
    }

    private static (DateTime? Utc, string? Id) ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return (null, null);
        }

        var pipe = cursor.LastIndexOf('|');
        if (pipe <= 0 || !DateTime.TryParse(cursor[..pipe], out var utc))
        {
            return (null, null);
        }

        return (utc.ToUniversalTime(), cursor[(pipe + 1)..]);
    }

    // ---- RF-002: attention transition → feed item ----

    /// <summary>
    /// Maps an attention state transition to a feed item, or null when the
    /// transition is not feed-worthy (spec: waiting_you, blocked, done).
    /// </summary>
    public static FeedItemRequest? FromAttentionSignal(
        AttentionSignal signal, AgentAttentionState state, string userId = "local") =>
        state switch
        {
            AgentAttentionState.WaitingYou => new FeedItemRequest(
                signal.Kind == AttentionSignalKind.Mailbox ? AgentFeedKind.Question : AgentFeedKind.Attention,
                $"needs you — {signal.SessionKey}", signal.Evidence,
                DeepLinkFor(signal.SessionKey), signal.SessionKey, signal.Cli,
                null, true, userId, signal.AtUtc),
            AgentAttentionState.Blocked => new FeedItemRequest(
                AgentFeedKind.Failure, $"blocked — {signal.SessionKey}", signal.Evidence,
                DeepLinkFor(signal.SessionKey), signal.SessionKey, signal.Cli,
                null, true, userId, signal.AtUtc),
            AgentAttentionState.Done => new FeedItemRequest(
                AgentFeedKind.Completion, $"done — {signal.SessionKey}", signal.Evidence,
                DeepLinkFor(signal.SessionKey), signal.SessionKey, signal.Cli,
                null, false, userId, signal.AtUtc),
            _ => null,
        };

    /// <summary>Deep-link from a session key — same surfaces as board cards.</summary>
    public static string DeepLinkFor(string? sessionKey) =>
        sessionKey switch
        {
            null or "" => "/agents?tab=dashboard",
            var k when k.StartsWith("task:", StringComparison.Ordinal)
                => $"/agents?tab=dashboard&focus={k[5..]}",
            var k when k.StartsWith("run:", StringComparison.Ordinal)
                => $"/cockpit",
            var k when k.StartsWith("term:", StringComparison.Ordinal)
                => "/terminal",
            var k when k.StartsWith("conv:", StringComparison.Ordinal)
                => $"/ai-chat?c={k[5..]}",
            _ => "/agents?tab=dashboard",
        };
}
