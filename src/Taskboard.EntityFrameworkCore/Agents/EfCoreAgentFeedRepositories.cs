using Microsoft.EntityFrameworkCore;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Agents;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Agents;

/// <summary>SPEC-20261020-agents-feed-notifications RF-001/RF-003/RF-008.</summary>
public sealed class EfCoreAgentFeedRepository : IAgentFeedRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreAgentFeedRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<AgentFeedItemDto> AddAsync(
        FeedItemRequest request, DateTime atUtc, CancellationToken ct = default)
    {
        var entity = AgentFeedItem.Create(
            request.UserId, request.Kind, request.Title, request.Body,
            request.DeepLink, request.SessionKey, request.Cli, request.IssueNumber,
            request.NeedsAttention, atUtc);
        _context.AgentFeedItems.Add(entity);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity, unread: true);
    }

    public async Task<AgentFeedItemDto?> GetAsync(string id, CancellationToken ct = default)
    {
        var entity = await _context.AgentFeedItems.FindAsync([id], ct).ConfigureAwait(false);
        return entity is null ? null : ToDto(entity, unread: true);
    }

    public async Task<IReadOnlyList<AgentFeedItemDto>> ListPageAsync(
        string userId, DateTime? beforeUtc, string? beforeId, int take,
        CancellationToken ct = default)
    {
        var query = _context.AgentFeedItems.Where(i => i.UserId == userId);
        if (beforeUtc is { } before && beforeId is { } bid)
        {
            query = query.Where(i => i.AtUtc < before || (i.AtUtc == before && i.Id.CompareTo(bid) < 0));
        }
        else if (beforeUtc is { } b)
        {
            query = query.Where(i => i.AtUtc < b);
        }

        var items = await query
            .OrderByDescending(i => i.AtUtc)
            .ThenByDescending(i => i.Id)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var flags = await UnreadFlagsAsync(
            userId, items.Select(i => i.Id).ToList(), ct).ConfigureAwait(false);
        return items
            .Select(i => ToDto(i, unread: !flags.TryGetValue(i.Id, out var manual) || manual))
            .ToList();
    }

    public async Task<AgentFeedItemDto?> FindDedupAsync(
        string userId, AgentFeedKind kind, string? sessionKey, string deepLink,
        DateTime sinceUtc, CancellationToken ct = default)
    {
        var entity = await _context.AgentFeedItems
            .Where(i => i.UserId == userId && i.Kind == kind
                && i.SessionKey == sessionKey && i.DeepLink == deepLink
                && i.AtUtc >= sinceUtc)
            .OrderByDescending(i => i.AtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return entity is null ? null : ToDto(entity, unread: true);
    }

    public async Task<AgentFeedItemDto?> MergeRepeatAsync(
        string id, DateTime atUtc, CancellationToken ct = default)
    {
        var entity = await _context.AgentFeedItems.FindAsync([id], ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        entity.MergeRepeat(atUtc);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(entity, unread: true);
    }

    public async Task<int> CountUnreadAsync(
        string userId, bool needsAttentionOnly, CancellationToken ct = default)
    {
        var items = _context.AgentFeedItems.Where(i => i.UserId == userId);
        if (needsAttentionOnly)
        {
            items = items.Where(i => i.NeedsAttention);
        }

        // Unread = no marker row OR marker.ManuallyUnread.
        var itemIds = items.Select(i => i.Id);
        var readIds = _context.UserFeedReads
            .Where(r => r.UserId == userId && !r.ManuallyUnread)
            .Select(r => r.ItemId);
        return await itemIds.Where(id => !readIds.Contains(id)).CountAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, bool>> UnreadFlagsAsync(
        string userId, IReadOnlyCollection<string> itemIds, CancellationToken ct = default) =>
        await _context.UserFeedReads
            .Where(r => r.UserId == userId && itemIds.Contains(r.ItemId))
            .ToDictionaryAsync(r => r.ItemId, r => r.ManuallyUnread, ct)
            .ConfigureAwait(false);

    /// <summary>RF-009: deletes expired items plus their read markers.</summary>
    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        var expiredIds = await _context.AgentFeedItems
            .Where(i => i.AtUtc < cutoffUtc)
            .Select(i => i.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (expiredIds.Count == 0)
        {
            return 0;
        }

        var reads = await _context.UserFeedReads
            .Where(r => expiredIds.Contains(r.ItemId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var items = await _context.AgentFeedItems
            .Where(i => expiredIds.Contains(i.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        _context.UserFeedReads.RemoveRange(reads);
        _context.AgentFeedItems.RemoveRange(items);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return items.Count;
    }

    private static AgentFeedItemDto ToDto(AgentFeedItem i, bool unread) =>
        new(i.Id, i.Kind, i.Title, i.Body, i.DeepLink, i.SessionKey, i.Cli,
            i.IssueNumber, i.AtUtc, i.NeedsAttention, i.RepeatCount, unread);
}

/// <summary>SPEC-20261020-agents-feed-notifications RF-004.</summary>
public sealed class EfCoreUserFeedReadRepository : IUserFeedReadRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreUserFeedReadRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task MarkReadAsync(
        string userId, string itemId, DateTime atUtc, CancellationToken ct = default)
    {
        var key = UserFeedRead.KeyOf(userId, itemId);
        var entity = await _context.UserFeedReads.FindAsync([key], ct).ConfigureAwait(false);
        if (entity is null)
        {
            entity = UserFeedRead.Create(userId, itemId, atUtc);
            _context.UserFeedReads.Add(entity);
        }
        else
        {
            entity.Touch(atUtc);
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkUnreadAsync(
        string userId, string itemId, DateTime atUtc, CancellationToken ct = default)
    {
        var key = UserFeedRead.KeyOf(userId, itemId);
        var entity = await _context.UserFeedReads.FindAsync([key], ct).ConfigureAwait(false);
        if (entity is null)
        {
            entity = UserFeedRead.Create(userId, itemId, atUtc);
            _context.UserFeedReads.Add(entity);
        }

        entity.MarkUnread();
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> DeleteAllForUserAsync(string userId, CancellationToken ct = default)
    {
        var entities = await _context.UserFeedReads
            .Where(r => r.UserId == userId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        _context.UserFeedReads.RemoveRange(entities);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return entities.Count;
    }
}

/// <summary>SPEC-20261020-agents-feed-notifications RF-006.</summary>
public sealed class EfCoreUserFeedPreferenceRepository : IUserFeedPreferenceRepository
{
    private readonly TaskboardDbContext _context;

    public EfCoreUserFeedPreferenceRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyDictionary<AgentFeedKind, FeedCategoryPreference>> GetForUserAsync(
        string userId, CancellationToken ct = default)
    {
        var rows = await _context.UserFeedPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.ToDictionary(
            p => p.Kind,
            p => new FeedCategoryPreference(p.Badge, p.Sound, p.Persist));
    }

    public async Task UpsertAsync(
        string userId, AgentFeedKind kind, FeedCategoryPreference pref,
        CancellationToken ct = default)
    {
        var key = UserFeedPreference.KeyOf(userId, kind);
        var entity = await _context.UserFeedPreferences
            .FindAsync([key], ct).ConfigureAwait(false);
        if (entity is null)
        {
            entity = UserFeedPreference.Create(
                userId, kind, pref.Badge, pref.Sound, pref.Persist);
            _context.UserFeedPreferences.Add(entity);
        }
        else
        {
            entity.Update(pref.Badge, pref.Sound, pref.Persist);
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
