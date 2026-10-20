using System.Text.RegularExpressions;

namespace Taskboard.Agents;

/// <summary>What a matched output tail pattern means (RF-005).</summary>
public enum AgentOutputPatternKind
{
    /// <summary>CLI asks permission (tool approval, y/n).</summary>
    Permission,

    /// <summary>CLI asks a question needing a free-form answer.</summary>
    Question,

    /// <summary>CLI sits at a prompt waiting for input.</summary>
    PromptInput,
}

/// <summary>A compiled tail-of-output matcher for one CLI.</summary>
public sealed record AgentOutputPattern(
    Regex Pattern, AgentOutputPatternKind Kind, int Priority);

/// <summary>
/// SPEC-20261020-agent-attention-model RF-005: per-CLI regex tables matched
/// against the last ≤4KB of output (ANSI-stripped). Generic fallbacks apply
/// to CLIs without a native table. Compiled once; matching is the weak
/// signal producer for <see cref="AttentionSignalKind.OutputPattern"/>.
/// </summary>
public static class AgentCliAttentionPatterns
{
    private static AgentOutputPattern P(string pattern, AgentOutputPatternKind kind, int priority) =>
        new(new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(100)), kind, priority);

    private static readonly AgentOutputPattern[] Generic =
    [
        P(@"\(y(?:es)?/[nN]o?\)\s*$", AgentOutputPatternKind.Permission, 50),
        P(@"\[(?:Y|y)/(?:N|n)\]\s*$", AgentOutputPatternKind.Permission, 50),
        P(@"do you want to (?:proceed|continue)", AgentOutputPatternKind.Permission, 60),
        P(@"press enter to (?:confirm|continue)", AgentOutputPatternKind.PromptInput, 40),
        P(@"approve\?\s*$", AgentOutputPatternKind.Permission, 55),
        P(@"\?\s*$", AgentOutputPatternKind.Question, 10),
    ];

    private static readonly IReadOnlyDictionary<AgentCliKind, AgentOutputPattern[]> ByCli =
        new Dictionary<AgentCliKind, AgentOutputPattern[]>
        {
            [AgentCliKind.Claude] =
            [
                P(@"do you want to proceed\?", AgentOutputPatternKind.Permission, 90),
                P(@"permission to use .+\?", AgentOutputPatternKind.Permission, 90),
                P(@"esc to cancel", AgentOutputPatternKind.PromptInput, 30),
                .. Generic,
            ],
            [AgentCliKind.Codex] =
            [
                P(@"allow .+\?\s*\[(?:y|n)", AgentOutputPatternKind.Permission, 90),
                P(@"approval required", AgentOutputPatternKind.Permission, 85),
                P(@"press (?:y|n|enter)", AgentOutputPatternKind.PromptInput, 40),
                .. Generic,
            ],
        };

    /// <summary>Patterns for a CLI kind — CLI table + generic fallbacks.</summary>
    public static IReadOnlyList<AgentOutputPattern> For(AgentCliKind kind) =>
        ByCli.TryGetValue(kind, out var table) ? table : Generic;

    /// <summary>Best match on the tail — highest priority wins.</summary>
    public static AgentOutputPattern? Match(AgentCliKind kind, string tail)
    {
        AgentOutputPattern? best = null;
        foreach (var pattern in For(kind))
        {
            if (pattern.Pattern.IsMatch(tail)
                && (best is null || pattern.Priority > best.Priority))
            {
                best = pattern;
            }
        }
        return best;
    }

    /// <summary>
    /// Binary name → kind ("claude" → Claude). Falls back to null when the
    /// command is not a known agent CLI.
    /// </summary>
    public static AgentCliKind? KindForBinary(string? binary)
    {
        if (string.IsNullOrWhiteSpace(binary))
        {
            return null;
        }
        var name = Path.GetFileName(binary)
            .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
        return name switch
        {
            "claude" => AgentCliKind.Claude,
            "codex" => AgentCliKind.Codex,
            "opencode" => AgentCliKind.OpenCode,
            "devin" => AgentCliKind.Devin,
            "agy" or "antigravity" => AgentCliKind.Antigravity,
            "kimi" => AgentCliKind.Kimi,
            "grok" => AgentCliKind.Grok,
            "aider" => AgentCliKind.Aider,
            "cline" => AgentCliKind.Cline,
            "cn" or "continue" => AgentCliKind.Continue,
            "copilot" => AgentCliKind.Copilot,
            "qwen" => AgentCliKind.Qwen,
            "kiro-cli" or "kiro" => AgentCliKind.Kiro,
            _ => null,
        };
    }
}
