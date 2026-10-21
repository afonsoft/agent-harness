using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Domain.Entities;
using Taskboard.Repositories;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261020-agent-hibernation: hibernate/resume pipeline. Singleton —
/// scoped collaborators (repos, guards, feed) are resolved per operation so
/// the 60s sweep and the manual endpoints share one path.
/// </summary>
public sealed class AgentHibernationService : IAgentHibernationService
{
    /// <summary>RF-002: cap of candidates hibernated per sweep tick.</summary>
    internal const int MaxCandidatesPerTick = 50;

    private readonly IServiceProvider _services;
    private readonly ISessionTerminator _terminator;
    private readonly ISessionRespawner _respawner;
    private readonly IAgentSessionIndexService _index;
    private readonly IAgentAttentionService _attention;
    private readonly ILogger<AgentHibernationService> _logger;

    public AgentHibernationService(
        IServiceProvider services,
        ISessionTerminator terminator,
        ISessionRespawner respawner,
        IAgentSessionIndexService index,
        IAgentAttentionService attention,
        ILogger<AgentHibernationService> logger)
    {
        _services = services;
        _terminator = terminator;
        _respawner = respawner;
        _index = index;
        _attention = attention;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> ScanOnceAsync(
        string userId, TimeSpan idleThreshold, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var states = scope.ServiceProvider.GetRequiredService<IAgentSessionStateRepository>();
        var records = scope.ServiceProvider.GetRequiredService<IAgentHibernationRepository>();

        var now = DateTime.UtcNow;
        var hibernated = await records.ListKeysAsync(ct).ConfigureAwait(false);
        var candidates = (await states.ListAsync(take: 200, ct: ct).ConfigureAwait(false))
            .Where(s => s.State is AgentAttentionState.Done or AgentAttentionState.Idle
                && !hibernated.Contains(s.SessionKey)
                && now - s.LastSignalAtUtc >= idleThreshold)
            .OrderBy(s => s.LastSignalAtUtc)
            .Take(MaxCandidatesPerTick)
            .ToList();

        var hibernatedCount = 0;
        foreach (var state in candidates)
        {
            if (await HibernateCoreAsync(scope.ServiceProvider, userId, state, now, ct)
                    .ConfigureAwait(false) is { Hibernated: true })
            {
                hibernatedCount++;
            }
        }

        return hibernatedCount;
    }

    /// <inheritdoc/>
    public async Task<HibernateOutcome> HibernateAsync(
        string userId, string sessionKey, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var states = scope.ServiceProvider.GetRequiredService<IAgentSessionStateRepository>();

        // Manual hibernate bypasses the threshold; a session without a state
        // row is treated as Done for the guard chain (live check below still
        // applies — nothing to kill means nothing to hibernate).
        var state = await states.GetAsync(sessionKey, ct).ConfigureAwait(false)
            ?? new AgentSessionStateDto(
                sessionKey, Cli: "unknown", AgentAttentionState.Done,
                null, DateTime.UtcNow, DateTime.UtcNow,
                AttentionSignalKind.Lifecycle, null, 0, 0);

        return await HibernateCoreAsync(
            scope.ServiceProvider, userId, state, DateTime.UtcNow, ct).ConfigureAwait(false);
    }

    /// <summary>Guard chain + kill + record + feed — shared by sweep and manual.</summary>
    private async Task<HibernateOutcome> HibernateCoreAsync(
        IServiceProvider sp, string userId, AgentSessionStateDto state,
        DateTime now, CancellationToken ct)
    {
        var records = sp.GetRequiredService<IAgentHibernationRepository>();
        var record = await records.GetAsync(state.SessionKey, ct).ConfigureAwait(false);

        foreach (var guard in sp.GetServices<IAgentHibernationGuard>())
        {
            if (await guard.CheckAsync(new(state, record, now, TimeSpan.Zero), ct)
                    .ConfigureAwait(false) is { } refusal)
            {
                return new HibernateOutcome(false, $"{guard.Name}: {refusal}");
            }
        }

        // P1 scope: only PTY-backed sessions hibernate — ACP threads are
        // managed by their own lifecycle.
        if (!state.SessionKey.StartsWith("term:", StringComparison.Ordinal))
        {
            return new HibernateOutcome(false, "session kind not hibernatable");
        }

        if (!_terminator.IsAlive(state.SessionKey))
        {
            return new HibernateOutcome(false, "session is not live");
        }

        // RF-006: the resume argv needs the CLI's native session id — the
        // terminal id is only the harness key. Resolve via the index join.
        var row = await _index.ResolveByLiveKeyAsync(state.SessionKey, ct)
            .ConfigureAwait(false);
        var argv = AgentCliResumeSupport.ResumeArgv(state.Cli, row?.SessionId ?? string.Empty);
        if (argv is null || row is null)
        {
            return new HibernateOutcome(false, "no resumable transcript found for session");
        }

        // Mark hibernated only after the process is dead and the PTY released.
        if (!await _terminator.TerminateAsync(state.SessionKey, ct).ConfigureAwait(false))
        {
            return new HibernateOutcome(false, "terminate failed");
        }

        await records.AddAsync(
            state.SessionKey, userId, state.Cli, JsonSerializer.Serialize(argv),
            row.SessionId, row.Cwd, state.LastSignalAtUtc, now, ct).ConfigureAwait(false);
        _index.Invalidate();

        await ProduceFeedAsync(sp, userId, AgentFeedKind.SessionHibernated,
            $"hibernated — {state.SessionKey}", state, needsAttention: false, ct)
            .ConfigureAwait(false);
        _logger.LogInformation("Session {SessionKey} hibernated (cli {Cli}).",
            state.SessionKey, state.Cli);
        return new HibernateOutcome(true);
    }

    /// <inheritdoc/>
    public async Task<ResumeOutcome> ResumeAsync(
        string userId, string sessionKey, bool auto = false, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var records = scope.ServiceProvider.GetRequiredService<IAgentHibernationRepository>();

        var record = await records.GetAsync(sessionKey, ct).ConfigureAwait(false);
        if (record is not { IsHibernated: true })
        {
            return new ResumeOutcome(false, sessionKey, "session is not hibernated");
        }

        // RF-007 idempotent: a live session under the key is reused, never
        // duplicated (the respawner also dedups on the deterministic id).
        if (!_terminator.IsAlive(sessionKey)
            && !await _respawner.RespawnAsync(
                sessionKey, record.UserId, record.ResumeArgv, record.Cwd, ct)
                .ConfigureAwait(false))
        {
            // RF-008: failed resume → blocked + feed, no automatic retry loop.
            await PublishSignalAsync(sessionKey, record.Cli,
                AgentAttentionState.Blocked, "resume_failed", ct).ConfigureAwait(false);
            await ProduceFeedAsync(scope.ServiceProvider, userId, AgentFeedKind.Failure,
                $"resume failed — {sessionKey}", state: null,
                needsAttention: true, ct, cli: record.Cli, key: sessionKey)
                .ConfigureAwait(false);
            return new ResumeOutcome(false, sessionKey, "respawn failed");
        }

        await records.MarkResumedAsync(sessionKey, auto, ct).ConfigureAwait(false);
        _index.Invalidate();
        await PublishSignalAsync(sessionKey, record.Cli,
            AgentAttentionState.Working, "lifecycle:resumed", ct).ConfigureAwait(false);
        await ProduceFeedAsync(scope.ServiceProvider, userId, AgentFeedKind.SessionResumed,
            $"resumed — {sessionKey}", state: null,
            needsAttention: false, ct, cli: record.Cli, key: sessionKey)
            .ConfigureAwait(false);
        _logger.LogInformation("Session {SessionKey} resumed.", sessionKey);
        return new ResumeOutcome(true, sessionKey);
    }

    /// <inheritdoc/>
    public async Task<FocusOutcome> FocusAsync(
        string userId, string sessionKey, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var records = scope.ServiceProvider.GetRequiredService<IAgentHibernationRepository>();

        var record = await records.GetAsync(sessionKey, ct).ConfigureAwait(false);
        if (record is not { IsHibernated: true })
        {
            return new FocusOutcome(false, false, false);
        }

        // RF-006: auto-resume only when the user pref allows — otherwise the
        // row stays resumable for the manual Resume action.
        var prefs = await ReadPrefsAsync(scope.ServiceProvider, ct).ConfigureAwait(false);
        if (!prefs.AutoResumeOnFocus)
        {
            return new FocusOutcome(true, false, true);
        }

        var resumed = await ResumeAsync(userId, sessionKey, auto: true, ct)
            .ConfigureAwait(false);
        return resumed.Resumed
            ? new FocusOutcome(true, true, false)
            : new FocusOutcome(true, false, true, resumed.Reason);
    }

    /// <inheritdoc/>
    public async Task<HibernationPrefsDto> GetPrefsAsync(
        string userId, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        return await ReadPrefsAsync(scope.ServiceProvider, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<HibernationPrefsDto> SavePrefsAsync(
        string userId, HibernationPrefsDto prefs, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var users = scope.ServiceProvider
            .GetRequiredService<IRepository<UserPreference>>();

        var clamped = new HibernationPrefsDto(
            Math.Clamp(prefs.IdleThresholdMinutes, 5, 1440),
            prefs.AutoResumeOnFocus);

        var user = (await users.ListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        if (user is null)
        {
            await users.AddAsync(new UserPreference(Guid.Empty)
            {
                AgentIdleThresholdMinutes = clamped.IdleThresholdMinutes,
                AutoResumeOnFocus = clamped.AutoResumeOnFocus,
            }, ct).ConfigureAwait(false);
        }
        else
        {
            user.AgentIdleThresholdMinutes = clamped.IdleThresholdMinutes;
            user.AutoResumeOnFocus = clamped.AutoResumeOnFocus;
            await users.UpdateAsync(user, ct).ConfigureAwait(false);
        }

        return clamped;
    }

    /// <summary>RF-010: effective threshold — user pref, clamped 5–1440, default 30.</summary>
    internal static async Task<HibernationPrefsDto> ReadPrefsAsync(
        IServiceProvider sp, CancellationToken ct)
    {
        var users = sp.GetRequiredService<IRepository<UserPreference>>();
        var user = (await users.ListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        return new HibernationPrefsDto(
            Math.Clamp(user?.AgentIdleThresholdMinutes ?? 30, 5, 1440),
            user?.AutoResumeOnFocus ?? true);
    }

    /// <inheritdoc/>
    public async Task<bool> SetPinnedAsync(
        string userId, string sessionKey, bool pinned, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var records = scope.ServiceProvider.GetRequiredService<IAgentHibernationRepository>();

        var record = await records.GetAsync(sessionKey, ct).ConfigureAwait(false);
        if (record is null)
        {
            if (!pinned)
            {
                return true;
            }

            // RF-009: pin a live session — resolve cli/cwd for the marker row.
            var row = await _index.ResolveByLiveKeyAsync(sessionKey, ct).ConfigureAwait(false);
            var cli = row?.Cli ?? sessionKey.Split(':')[0];
            await records.PinOnlyAsync(
                sessionKey, userId, cli, row?.SessionId, row?.Cwd, DateTime.UtcNow, ct)
                .ConfigureAwait(false);
            return true;
        }

        await records.SetPinnedAsync(sessionKey, pinned, ct).ConfigureAwait(false);
        return true;
    }

    private async Task PublishSignalAsync(
        string sessionKey, string cli, AgentAttentionState state,
        string reason, CancellationToken ct)
    {
        try
        {
            await _attention.PublishAsync(
                new AttentionSignal(sessionKey, cli,
                    AttentionSignalKind.Lifecycle, state, reason), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Attention publish is advisory — the hibernation record is the
            // source of truth for the lifecycle.
            _logger.LogDebug(ex, "Attention publish failed for {SessionKey}.", sessionKey);
        }
    }

    private async Task ProduceFeedAsync(
        IServiceProvider sp, string userId, AgentFeedKind kind, string title,
        AgentSessionStateDto? state, bool needsAttention, CancellationToken ct,
        string? cli = null, string? key = null)
    {
        try
        {
            var sessionKey = key ?? state?.SessionKey;
            await sp.GetRequiredService<IAgentFeedService>().ProduceAsync(
                new FeedItemRequest(kind, title, null,
                    AgentFeedService.DeepLinkFor(sessionKey), sessionKey,
                    cli ?? state?.Cli, null, needsAttention, userId), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Feed is advisory — never blocks the lifecycle op.
            _logger.LogDebug(ex, "Feed produce failed ({Kind}).", kind);
        }
    }
}
