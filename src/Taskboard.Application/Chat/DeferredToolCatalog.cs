using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261023-p0-agent-hardening RF-D01/D02/D03: session-scoped deferred
/// tool catalog — plans schema-vs-catalog partition per conversation, TTL-caches
/// availability probes (60s, 2s per-probe bound, fail-closed) and records
/// append-only promotions.
/// </summary>
public sealed class DeferredToolCatalog(
    IConfiguration configuration,
    TimeProvider? clock = null,
    TimeSpan? probeTtl = null) : IDeferredToolCatalog
{
    /// <summary><c>Taskboard:Chat:DeferredTools</c> — JSON name list replacing <see cref="DefaultDeferred"/>.</summary>
    public const string ConfigKey = "Taskboard:Chat:DeferredTools";

    /// <summary>RF-D03 default deferred set — heavy/conditional schemas.</summary>
    public static readonly FrozenSet<string> DefaultDeferred =
        new[] { "browser_use", "generate_image", "code_interpreter" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Discovery meta-tools can never be deferred — they are the activation path.</summary>
    public static readonly FrozenSet<string> MetaTools =
        new[] { "tool_search", "tool_activate" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly TimeSpan ProbeBound = TimeSpan.FromSeconds(2);

    private sealed class ConvState
    {
        public Dictionary<string, DeferredToolEntry> Deferred { get; set; } = new(StringComparer.Ordinal);
        public List<string> Promoted { get; } = [];
        public Dictionary<string, (bool Available, DateTimeOffset At)> Probes { get; } = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, IChatTool>? ToolSet { get; set; }
    }

    private readonly ConcurrentDictionary<string, ConvState> _conversations = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly TimeSpan _probeTtl = probeTtl ?? TimeSpan.FromSeconds(60);

    public async Task<ChatToolPlan> PlanAsync(
        string conversationId,
        IReadOnlyDictionary<string, IChatTool> toolSet,
        ChatToolContext probeContext,
        CancellationToken cancellationToken)
    {
        var state = _conversations.GetOrAdd(conversationId, _ => new ConvState());
        var configured = DeferredNames(configuration);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var deferred = new Dictionary<string, DeferredToolEntry>(StringComparer.Ordinal);

        foreach (var (name, tool) in toolSet.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (MetaTools.Contains(name))
            {
                active.Add(name);
                continue;
            }

            var available = await ProbeAsync(state, name, tool, probeContext, cancellationToken)
                .ConfigureAwait(false);
            if (!available || configured.Contains(name))
            {
                deferred[name] = new DeferredToolEntry(name, tool.Description, available);
            }
            else
            {
                active.Add(name);
            }
        }

        lock (state)
        {
            state.ToolSet = toolSet;
            state.Deferred = deferred;
        }

        return new ChatToolPlan(active, deferred.Values.OrderBy(e => e.Name, StringComparer.Ordinal).ToList());
    }

    public IReadOnlyList<DeferredToolEntry> Deferred(string conversationId) =>
        _conversations.TryGetValue(conversationId, out var state)
            ? state.Deferred.Values.OrderBy(e => e.Name, StringComparer.Ordinal).ToList()
            : [];

    public IReadOnlyList<string> PromotedNames(string conversationId) =>
        _conversations.TryGetValue(conversationId, out var state)
            ? state.Promoted.ToList()
            : [];

    public ToolActivateResult Activate(string conversationId, string name)
    {
        var state = _conversations.GetOrAdd(conversationId, _ => new ConvState());
        lock (state)
        {
            if (name is "*" or "all")
            {
                var added = 0;
                foreach (var entry in state.Deferred.Values.Where(e => e.Available))
                {
                    if (!state.Promoted.Contains(entry.Name, StringComparer.Ordinal))
                    {
                        state.Promoted.Add(entry.Name);
                        added++;
                    }
                }

                return new ToolActivateResult(true, null, added);
            }

            if (!state.Deferred.TryGetValue(name, out var deferred))
            {
                var error = state.ToolSet?.ContainsKey(name) == true
                    ? $"tool '{name}' is already active"
                    : $"tool '{name}' is not deferred or unknown — call tool_search to list deferred tools";
                return new ToolActivateResult(false, error);
            }

            if (!deferred.Available)
            {
                return new ToolActivateResult(
                    false, $"tool '{name}' is unavailable (availability probe failed) and cannot be activated");
            }

            if (!state.Promoted.Contains(name, StringComparer.Ordinal))
            {
                state.Promoted.Add(name);
            }

            return new ToolActivateResult(true, null, 1);
        }
    }

    public IReadOnlyList<DeferredToolEntry> Search(string conversationId, string? query)
    {
        var entries = Deferred(conversationId);
        if (string.IsNullOrWhiteSpace(query))
        {
            return entries;
        }

        var q = query.Trim();
        return entries
            .Where(e => e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();
    }

    /// <summary>JSON name list from <see cref="ConfigKey"/>; defaults when absent/invalid.</summary>
    private static ISet<string> DeferredNames(IConfiguration configuration)
    {
        var raw = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(DefaultDeferred, StringComparer.Ordinal);
        }

        try
        {
            var names = JsonSerializer.Deserialize<List<string>>(raw);
            return names is null
                ? new HashSet<string>(DefaultDeferred, StringComparer.Ordinal)
                : new HashSet<string>(names, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(DefaultDeferred, StringComparer.Ordinal);
        }
    }

    /// <summary>TTL-cached probe — failure/timeout degrade to unavailable (fail-closed).</summary>
    private async Task<bool> ProbeAsync(
        ConvState state, string name, IChatTool tool,
        ChatToolContext probeContext, CancellationToken cancellationToken)
    {
        lock (state)
        {
            if (state.Probes.TryGetValue(name, out var cached)
                && _clock.GetUtcNow() - cached.At < _probeTtl)
            {
                return cached.Available;
            }
        }

        bool available;
        try
        {
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bound.CancelAfter(ProbeBound);
            available = await tool.IsAvailableAsync(probeContext, bound.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            available = false;
        }
        catch (Exception)
        {
            available = false;
        }

        lock (state)
        {
            state.Probes[name] = (available, _clock.GetUtcNow());
        }

        return available;
    }
}
