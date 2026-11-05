using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace Taskboard.Integrations.Agents.Attention;

/// <summary>
/// SPEC-20261104-agent-attention-manifests RF-001: a detection manifest parsed
/// from TOML and compiled to a gate tree. Manifests are data — parsing
/// validates the schema (deny unknown keys), compiles regexes with a 100ms
/// NonBacktracking timeout and enforces <see cref="MaxRulesPerManifest"/>.
/// </summary>
public sealed class AgentDetectionManifest
{
    public const int MaxRulesPerManifest = 128;

    private static readonly HashSet<string> ManifestKeys =
        new(StringComparer.Ordinal) { "id", "version", "min_engine_version", "updated_at", "aliases", "rules" };

    private static readonly HashSet<string> RuleKeys =
        new(StringComparer.Ordinal)
        {
            "id", "state", "priority", "region",
            "visible_idle", "visible_blocker", "visible_working",
            "skip_state_update", "contains", "regex", "line_regex",
            "all", "any", "not",
        };

    private static readonly HashSet<string> GateKeys =
        new(StringComparer.Ordinal) { "contains", "regex", "line_regex", "all", "any", "not" };

    private static readonly HashSet<string> States =
        new(StringComparer.Ordinal)
        { "idle", "working", "done", "blocked", "error", "waiting_you", "unknown" };

    public required string Id { get; init; }
    public string? Version { get; init; }
    public int MinEngineVersion { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public IReadOnlyList<DetectionRule> Rules { get; init; } = [];

    /// <summary>Parses + compiles a manifest; throws <see cref="ManifestException"/> on any schema violation.</summary>
    public static AgentDetectionManifest Parse(string toml, string sourceName)
    {
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(toml)
                ?? throw new ManifestException($"{sourceName}: empty manifest");
        }
        catch (ManifestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ManifestException($"{sourceName}: invalid TOML — {ex.Message}");
        }

        RejectUnknownKeys(table, ManifestKeys, sourceName);

        var id = GetString(table, "id", sourceName, required: true)!;
        var version = GetString(table, "version", sourceName, required: false);
        var minEngine = GetInt(table, "min_engine_version", sourceName) ?? 1;
        var aliases = GetStringArray(table, "aliases", sourceName);

        var rules = new List<DetectionRule>();
        if (table.TryGetValue("rules", out var rulesObj) && rulesObj is TomlTableArray ruleTables)
        {
            if (ruleTables.Count > MaxRulesPerManifest)
            {
                throw new ManifestException(
                    $"{sourceName}: manifest has {ruleTables.Count} rules, max is {MaxRulesPerManifest}");
            }

            foreach (var ruleTable in ruleTables)
            {
                rules.Add(ParseRule(ruleTable, sourceName));
            }
        }
        else if (rulesObj is not null)
        {
            throw new ManifestException($"{sourceName}: 'rules' must be an array of tables");
        }

        return new AgentDetectionManifest
        {
            Id = id,
            Version = version,
            MinEngineVersion = minEngine,
            Aliases = aliases,
            // Priority-ordered, stable — first full match wins.
            Rules = [.. rules.OrderByDescending(r => r.Priority)],
        };
    }

    private static DetectionRule ParseRule(TomlTable table, string sourceName)
    {
        RejectUnknownKeys(table, RuleKeys, sourceName);
        var id = GetString(table, "id", sourceName, required: true)!;
        var state = GetString(table, "state", sourceName, required: true)!;
        if (!States.Contains(state))
        {
            throw new ManifestException($"{sourceName}: rule '{id}' has invalid state '{state}'");
        }

        return new DetectionRule
        {
            Id = id,
            State = state,
            Priority = GetInt(table, "priority", sourceName) ?? 0,
            Region = GetString(table, "region", sourceName, required: false) ?? "whole_recent",
            VisibleIdle = GetBool(table, "visible_idle", sourceName),
            VisibleBlocker = GetBool(table, "visible_blocker", sourceName),
            VisibleWorking = GetBool(table, "visible_working", sourceName),
            SkipStateUpdate = GetBool(table, "skip_state_update", sourceName),
            Gate = ParseGate(table, $"{sourceName}: rule '{id}'", topLevel: true),
        };
    }

    private static DetectionGate ParseGate(TomlTable table, string context, bool topLevel = false)
    {
        // Top level is the rule table itself — its keys were already validated
        // against the rule schema; nested gate tables must be gate-only.
        if (!topLevel)
        {
            RejectUnknownKeys(table, GateKeys, context);
        }
        var gate = new DetectionGate
        {
            Contains = GetStringArray(table, "contains", context)
                .Select(s => s.ToLowerInvariant()).ToList(),
            Regexes = GetStringArray(table, "regex", context)
                .Select(p => Compile(p, context, "regex")).ToList(),
            LineRegexes = GetStringArray(table, "line_regex", context)
                .Select(p => Compile(p, context, "line_regex")).ToList(),
            All = ParseGateArray(table, "all", context),
            Any = ParseGateArray(table, "any", context),
            Not = ParseGateArray(table, "not", context),
        };

        if (table.ContainsKey("not") && gate.Not.Count == 0)
        {
            throw new ManifestException($"{context}: 'not' gate must not be empty");
        }

        return gate;
    }

    private static IReadOnlyList<DetectionGate> ParseGateArray(TomlTable table, string key, string context)
    {
        if (!table.TryGetValue(key, out var obj))
        {
            return [];
        }

        // Inline arrays of gate tables materialize as TomlArray of TomlTable.
        if (obj is not TomlArray gates)
        {
            throw new ManifestException($"{context}: '{key}' must be an array of gate tables");
        }

        var list = new List<DetectionGate>(gates.Count);
        foreach (var item in gates)
        {
            if (item is not TomlTable gateTable)
            {
                throw new ManifestException($"{context}: '{key}' entries must be tables");
            }
            list.Add(ParseGate(gateTable, context));
        }
        return list;
    }

    private static Regex Compile(string pattern, string context, string field)
    {
        try
        {
            // RF §4: NonBacktracking + 100ms — manifest regexes can never stall
            // eval. Case-sensitive like herdr; manifests opt in via (?i).
            return new Regex(pattern,
                RegexOptions.Compiled | RegexOptions.NonBacktracking,
                TimeSpan.FromMilliseconds(100));
        }
        catch (Exception ex)
        {
            throw new ManifestException($"{context}: invalid {field} pattern '{pattern}' — {ex.Message}");
        }
    }

    private static void RejectUnknownKeys(TomlTable table, HashSet<string> allowed, string context)
    {
        foreach (var key in table.Keys)
        {
            if (!allowed.Contains(key))
            {
                throw new ManifestException($"{context}: unknown key '{key}'");
            }
        }
    }

    private static string? GetString(TomlTable table, string key, string context, bool required)
    {
        if (!table.TryGetValue(key, out var obj))
        {
            return required ? throw new ManifestException($"{context}: missing required '{key}'") : null;
        }

        return obj is string s
            ? s
            : throw new ManifestException($"{context}: '{key}' must be a string");
    }

    private static int? GetInt(TomlTable table, string key, string context)
    {
        if (!table.TryGetValue(key, out var obj))
        {
            return null;
        }

        return obj is long l
            ? (int)l
            : throw new ManifestException($"{context}: '{key}' must be an integer");
    }

    private static bool GetBool(TomlTable table, string key, string context) =>
        table.TryGetValue(key, out var obj)
            ? obj is bool b
                ? b
                : throw new ManifestException($"{context}: '{key}' must be a boolean")
            : false;

    private static IReadOnlyList<string> GetStringArray(TomlTable table, string key, string context)
    {
        if (!table.TryGetValue(key, out var obj))
        {
            return [];
        }

        if (obj is not TomlArray arr)
        {
            throw new ManifestException($"{context}: '{key}' must be a string array");
        }

        var list = new List<string>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not string s)
            {
                throw new ManifestException($"{context}: '{key}' must contain only strings");
            }
            list.Add(s);
        }
        return list;
    }
}

/// <summary>A manifest that failed schema validation — caller falls back + warns.</summary>
public sealed class ManifestException(string message) : Exception(message);

/// <summary>One compiled manifest rule.</summary>
public sealed class DetectionRule
{
    public required string Id { get; init; }
    public required string State { get; init; }
    public int Priority { get; init; }
    public string Region { get; init; } = "whole_recent";
    public bool VisibleIdle { get; init; }
    public bool VisibleBlocker { get; init; }
    public bool VisibleWorking { get; init; }
    public bool SkipStateUpdate { get; init; }
    public required DetectionGate Gate { get; init; }

    public IReadOnlyList<string> EvidenceFlags()
    {
        var flags = new List<string>(3);
        if (VisibleBlocker) flags.Add("visible_blocker");
        if (VisibleWorking) flags.Add("visible_working");
        if (VisibleIdle) flags.Add("visible_idle");
        return flags;
    }
}

/// <summary>
/// A gate node: contains/regex/line_regex are AND; <see cref="All"/> is AND,
/// <see cref="Any"/> is OR (empty = pass), <see cref="Not"/> vetoes when any
/// nested gate matches. Mirrors herdr's compiled_gate_matches semantics.
/// </summary>
public sealed class DetectionGate
{
    public IReadOnlyList<string> Contains { get; init; } = [];
    public IReadOnlyList<Regex> Regexes { get; init; } = [];
    public IReadOnlyList<Regex> LineRegexes { get; init; } = [];
    public IReadOnlyList<DetectionGate> All { get; init; } = [];
    public IReadOnlyList<DetectionGate> Any { get; init; } = [];
    public IReadOnlyList<DetectionGate> Not { get; init; } = [];
}
