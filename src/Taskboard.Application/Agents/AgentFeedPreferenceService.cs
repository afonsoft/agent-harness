using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261020-agents-feed-notifications RF-006: merged per-user category
/// preferences — stored rows over <see cref="AgentFeedDefaults"/>. Singleton
/// resolving the scoped repository per call; per-user map cached in-memory
/// and invalidated on write (NFR).
/// </summary>
public sealed class AgentFeedPreferenceService : IAgentFeedPreferenceService
{
    private readonly IServiceProvider _services;
    private readonly ConcurrentDictionary<string, AgentFeedPreferencesDto> _cache = new();

    public AgentFeedPreferenceService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<FeedCategoryPreference> ResolveAsync(
        string userId, AgentFeedKind kind, CancellationToken ct = default)
    {
        var all = await GetForUserAsync(userId, ct).ConfigureAwait(false);
        return all.Categories[kind];
    }

    public async Task<AgentFeedPreferencesDto> GetForUserAsync(
        string userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserFeedPreferenceRepository>();
        var stored = await repo.GetForUserAsync(userId, ct).ConfigureAwait(false);

        var merged = new Dictionary<AgentFeedKind, FeedCategoryPreference>();
        foreach (var kind in Enum.GetValues<AgentFeedKind>())
        {
            merged[kind] = stored.TryGetValue(kind, out var pref)
                ? pref
                : AgentFeedDefaults.For(kind);
        }

        var dto = new AgentFeedPreferencesDto(merged);
        _cache[userId] = dto;
        return dto;
    }

    public async Task<AgentFeedPreferencesDto> SaveForUserAsync(
        string userId,
        IReadOnlyDictionary<AgentFeedKind, FeedCategoryPreference> categories,
        CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserFeedPreferenceRepository>();
        foreach (var (kind, pref) in categories)
        {
            await repo.UpsertAsync(userId, kind, pref, ct).ConfigureAwait(false);
        }

        _cache.TryRemove(userId, out _);
        return await GetForUserAsync(userId, ct).ConfigureAwait(false);
    }
}
