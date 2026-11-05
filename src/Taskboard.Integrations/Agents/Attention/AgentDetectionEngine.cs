using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents.Attention;

/// <summary>
/// SPEC-20261104-agent-attention-manifests RF-002: pure evaluator — snapshot
/// in, verdict out. Holds an immutable <see cref="DetectionManifestSet"/>
/// swapped atomically by <see cref="Reload"/>; no locks on the eval path.
/// </summary>
public sealed class AgentDetectionEngine : IAgentDetectionEngine
{
    /// <summary>Engine schema version — manifests requiring more are skipped.</summary>
    public const int EngineVersion = 1;

    /// <summary>Fallback manifest id used when the CLI has no own manifest.</summary>
    public const string FallbackManifestId = "shell";

    private readonly AgentDetectionManifestLoader _loader;
    private volatile DetectionManifestSet _set;

    public AgentDetectionEngine(AgentDetectionManifestLoader loader)
    {
        _loader = loader;
        _set = loader.Load();
    }

    /// <summary>Test seam — engine over an already-built set.</summary>
    internal AgentDetectionEngine(DetectionManifestSet set, AgentDetectionManifestLoader loader)
    {
        _loader = loader;
        _set = set;
    }

    /// <summary>Current warnings collected at the last load (explain/observability).</summary>
    public IReadOnlyList<string> LoadWarnings => _set.Warnings;

    public DetectionVerdict Evaluate(DetectionSnapshot snapshot, string? cli)
    {
        var set = _set;
        var warnings = new List<string>(set.Warnings);

        var loaded = Resolve(set, cli, out var resolutionNote);
        if (resolutionNote is not null)
        {
            warnings.Add(resolutionNote);
        }

        if (loaded is null)
        {
            return new DetectionVerdict(
                null, null, null, null,
                DetectionSources.None, null, [], [], warnings);
        }

        var manifest = loaded.Manifest;
        var evaluated = new List<EvaluatedRuleDto>(manifest.Rules.Count);
        DetectionRule? matched = null;
        string? matchedRegion = null;
        var regionCache = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rule in manifest.Rules)
        {
            if (!regionCache.TryGetValue(rule.Region, out var regionText))
            {
                regionText = DetectionRegions.Extract(snapshot, rule.Region, out var regionWarning);
                regionCache[rule.Region] = regionText;
                if (regionWarning is not null)
                {
                    warnings.Add($"rule '{rule.Id}': {regionWarning}");
                }
            }

            var ok = GateMatches(rule.Gate, regionText, out var failedGate);
            evaluated.Add(new EvaluatedRuleDto(rule.Id, rule.Priority, rule.Region, ok, failedGate));
            if (ok && matched is null)
            {
                matched = rule;
                matchedRegion = rule.Region;
            }
        }

        if (matched is null)
        {
            return new DetectionVerdict(
                null, null, null, null,
                loaded.Source, manifest.Id, [], evaluated, warnings);
        }

        var state = matched.SkipStateUpdate ? null : MapState(matched);
        return new DetectionVerdict(
            state, matched.Id, matched.Priority, matchedRegion,
            loaded.Source, manifest.Id, matched.EvidenceFlags(), evaluated, warnings);
    }

    public DetectionReloadResult Reload()
    {
        var set = _loader.Load();
        _set = set; // atomic swap — evaluators in flight keep the old set
        return new DetectionReloadResult(set.ById.Count, set.OverrideCount, set.Warnings);
    }

    private LoadedDetectionManifest? Resolve(
        DetectionManifestSet set, string? cli, out string? note)
    {
        note = null;
        var id = NormalizeCli(cli);
        if (id is not null
            && set.Aliases.TryGetValue(id, out var manifestId)
            && set.ById.TryGetValue(manifestId, out var direct))
        {
            return direct;
        }

        if (set.ById.TryGetValue(FallbackManifestId, out var fallback))
        {
            if (id is not null)
            {
                note = $"no manifest for '{id}' — using '{FallbackManifestId}' fallback";
            }
            return fallback;
        }

        return null;
    }

    /// <summary>"claude" | "/usr/bin/claude" | "codex.exe" → normalized manifest id.</summary>
    internal static string? NormalizeCli(string? cli)
    {
        if (string.IsNullOrWhiteSpace(cli))
        {
            return null;
        }

        var name = Path.GetFileName(cli.Trim())
            .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
        return name.Length == 0 ? null : name;
    }

    /// <summary>Manifest state → attention state; visible_blocker = needs-you prompt.</summary>
    private static AgentAttentionState? MapState(DetectionRule rule) => rule.State switch
    {
        "idle" => AgentAttentionState.Idle,
        "working" => AgentAttentionState.Working,
        "done" => AgentAttentionState.Done,
        "blocked" => rule.VisibleBlocker ? AgentAttentionState.WaitingYou : AgentAttentionState.Blocked,
        "waiting_you" => AgentAttentionState.WaitingYou,
        "error" => AgentAttentionState.Blocked,
        _ => null, // "unknown"
    };

    /// <summary>
    /// Recursive gate eval mirroring herdr's compiled_gate_matches: contains/
    /// regex/line_regex are AND; <c>all</c> AND, <c>any</c> OR (empty passes),
    /// <c>not</c> vetoes. On failure <paramref name="failedGate"/> describes the
    /// first failing primitive for explain.
    /// </summary>
    internal static bool GateMatches(
        DetectionGate gate, string text, out string? failedGate)
    {
        var lower = text.ToLowerInvariant();
        return GateMatches(gate, text, lower, prefix: null, out failedGate);
    }

    private static bool GateMatches(
        DetectionGate gate, string text, string lower, string? prefix, out string? failedGate)
    {
        foreach (var needle in gate.Contains)
        {
            if (!lower.Contains(needle, StringComparison.Ordinal))
            {
                failedGate = $"{prefix}contains: \"{needle}\"";
                return false;
            }
        }

        foreach (var regex in gate.Regexes)
        {
            if (!regex.IsMatch(text))
            {
                failedGate = $"{prefix}regex: /{regex}/";
                return false;
            }
        }

        foreach (var regex in gate.LineRegexes)
        {
            var matched = false;
            var start = 0;
            while (!matched && start <= text.Length)
            {
                var end = text.IndexOf('\n', start);
                var len = (end < 0 ? text.Length : end) - start;
                if (regex.IsMatch(text.AsSpan(start, len)))
                {
                    matched = true;
                }
                if (end < 0)
                {
                    break;
                }
                start = end + 1;
            }

            if (!matched)
            {
                failedGate = $"{prefix}line_regex: /{regex}/";
                return false;
            }
        }

        foreach (var nested in gate.All)
        {
            if (!GateMatches(nested, text, lower, "all:", out failedGate))
            {
                return false;
            }
        }

        if (gate.Any.Count > 0)
        {
            string? innerFail = null;
            var anyMatched = false;
            foreach (var nested in gate.Any)
            {
                if (GateMatches(nested, text, lower, null, out var fail))
                {
                    anyMatched = true;
                    break;
                }
                innerFail ??= fail;
            }

            if (!anyMatched)
            {
                failedGate = $"any: {innerFail ?? "empty"}";
                return false;
            }
        }

        foreach (var nested in gate.Not)
        {
            if (GateMatches(nested, text, lower, null, out _))
            {
                failedGate = $"{prefix}not gate matched";
                return false;
            }
        }

        failedGate = null;
        return true;
    }
}
