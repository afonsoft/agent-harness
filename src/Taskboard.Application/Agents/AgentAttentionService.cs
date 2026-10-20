using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Application.Agents;

/// <summary>
/// SPEC-20261020-agent-attention-model RF-002: pure transition machine with
/// hysteresis. Strong signals (hook/lifecycle/mailbox) commit immediately;
/// weak signals (output_pattern/idle) need <see cref="WeakSignalsRequired"/>
/// consecutive observations before they may move the state. Silence never
/// means death — <see cref="AgentAttentionState.Exited"/> only comes from a
/// lifecycle exit signal.
/// </summary>
public static class AgentAttentionEvaluator
{
    public const int WeakSignalsRequired = 2;

    private static readonly IReadOnlySet<AttentionSignalKind> Strong =
        new HashSet<AttentionSignalKind>
        {
            AttentionSignalKind.Hook,
            AttentionSignalKind.Mailbox,
            AttentionSignalKind.Lifecycle,
        };

    public static bool IsStrong(AttentionSignalKind kind) => Strong.Contains(kind);

    /// <summary>
    /// Returns the state to commit, or null when the signal does not clear
    /// hysteresis. <paramref name="weakCountAfter"/> tells the store whether to
    /// bump or reset the weak-signal counter.
    /// </summary>
    public static AgentAttentionState? Evaluate(
        AgentSessionStateDto? current, AttentionSignal signal,
        out int weakCountAfter, out bool resetWeak)
    {
        resetWeak = false;
        var prior = current?.State ?? AgentAttentionState.Working;
        var weakCount = current?.WeakSignalCount ?? 0;
        var next = signal.SuggestedState;

        if (IsStrong(signal.Kind))
        {
            weakCountAfter = 0;
            resetWeak = weakCount > 0;
            return next;
        }

        // Weak path: idle/output-pattern — require consecutive observations.
        weakCountAfter = weakCount + 1;
        if (next is AgentAttentionState.WaitingYou or AgentAttentionState.Blocked
            && weakCountAfter < WeakSignalsRequired)
        {
            return null;
        }

        // Weak "alive" signals can only pull out of Idle, never out of
        // Waiting/Blocked (those need a strong signal to leave).
        if (next == AgentAttentionState.Working
            && prior is AgentAttentionState.WaitingYou or AgentAttentionState.Blocked)
        {
            return null;
        }

        return next;
    }
}

/// <summary>
/// RF-002/RF-008: serialized per-session signal pipeline → store → broadcast.
/// Singleton — resolves the scoped repository per call; per-session locks
/// live here so parallel producers cannot race a session key.
/// </summary>
public sealed class AgentAttentionService : IAgentAttentionService
{
    private readonly IServiceProvider _services;
    private readonly IAgentAttentionBroadcaster _broadcaster;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public AgentAttentionService(
        IServiceProvider services,
        IAgentAttentionBroadcaster broadcaster,
        TimeProvider? timeProvider = null)
    {
        _services = services;
        _broadcaster = broadcaster;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<AgentSessionStateDto> PublishAsync(
        AttentionSignal signal, CancellationToken ct = default)
    {
        var atUtc = signal.AtUtc ?? _time.GetUtcNow().UtcDateTime;
        var gate = _locks.GetOrAdd(signal.SessionKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var states = scope.ServiceProvider.GetRequiredService<IAgentSessionStateRepository>();

            var current = await states.GetAsync(signal.SessionKey, ct).ConfigureAwait(false);
            if (current is null)
            {
                await states.EnsureAsync(signal.SessionKey, signal.Cli, atUtc, ct).ConfigureAwait(false);
            }

            var next = AgentAttentionEvaluator.Evaluate(
                current, signal, out var weakAfter, out var resetWeak);
            var (state, changed) = await states.ApplyAsync(
                signal, next ?? current?.State ?? AgentAttentionState.Working,
                weakAfter, resetWeak, atUtc, ct).ConfigureAwait(false);

            if (changed)
            {
                await _broadcaster.BroadcastAsync(state, ct).ConfigureAwait(false);
            }

            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentSessionStateDto>> SnapshotAsync(
        IReadOnlyCollection<string>? keys = null, CancellationToken ct = default)
    {
        await using var scope = _services.CreateAsyncScope();
        var states = scope.ServiceProvider.GetRequiredService<IAgentSessionStateRepository>();
        return await states.ListAsync(keys, ct: ct).ConfigureAwait(false);
    }

    public async Task<int> SweepUnverifiableAsync(TimeSpan window, CancellationToken ct = default)
    {
        var cutoff = _time.GetUtcNow().UtcDateTime - window;
        IReadOnlyList<AgentSessionStateDto> silent;
        await using (var scope = _services.CreateAsyncScope())
        {
            var states = scope.ServiceProvider.GetRequiredService<IAgentSessionStateRepository>();
            silent = await states.ListSilentAsync(cutoff, ct: ct).ConfigureAwait(false);
        }

        var swept = 0;
        foreach (var session in silent.Where(s =>
            s.State is AgentAttentionState.Working or AgentAttentionState.Idle))
        {
            await PublishAsync(
                new AttentionSignal(
                    session.SessionKey, session.Cli, AttentionSignalKind.Idle,
                    AgentAttentionState.Unverifiable, "no-signal",
                    $"silent since {session.LastSignalAtUtc:O}", _time.GetUtcNow().UtcDateTime),
                ct).ConfigureAwait(false);
            swept++;
        }

        return swept;
    }
}
