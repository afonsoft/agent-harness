using System.Text.Json;

namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-E02: chat lifecycle hook event names.
/// <c>tool.before</c>/<c>tool.after</c> carry the tool name as subject;
/// <c>mcp.call</c> carries <c>mcp_{server}_{tool}</c>; <c>run.start</c>/
/// <c>run.end</c> carry <c>"run"</c>.
/// </summary>
public static class ChatHookEvents
{
    public const string ToolBefore = "tool.before";
    public const string ToolAfter = "tool.after";
    public const string RunStart = "run.start";
    public const string RunEnd = "run.end";
    public const string McpCall = "mcp.call";
}

/// <summary>Hook verdict kinds — <see cref="Block"/> &gt; <see cref="Warn"/> &gt; <see cref="Allow"/>.</summary>
public enum HookVerdictKind
{
    Allow = 0,
    Warn = 1,
    Block = 2,
}

/// <summary>
/// One hook's decision on a lifecycle event. <see cref="Hook"/> is the hook id
/// for attribution — when a hook leaves it null the engine stamps the caller's
/// <see cref="IChatHook.Name"/>.
/// </summary>
public sealed record HookVerdict(HookVerdictKind Kind, string? Reason = null, string? Hook = null)
{
    public static HookVerdict Allow { get; } = new(HookVerdictKind.Allow);
    public static HookVerdict Warn(string reason, string? hook = null) => new(HookVerdictKind.Warn, reason, hook);
    public static HookVerdict Block(string reason, string? hook = null) => new(HookVerdictKind.Block, reason, hook);
}

/// <summary>
/// Payload handed to hooks. <see cref="Arguments"/> carries the tool call args
/// (<c>tool.before</c>/<c>mcp.call</c>); <see cref="ResultHead"/> the truncated
/// tool result (<c>tool.after</c>); <see cref="Meta"/> event-specific extras
/// (e.g. <c>server</c>/<c>healthy</c>/<c>error</c> on <c>mcp.call</c>,
/// <c>provider</c>/<c>model</c>/<c>error</c> on run events).
/// </summary>
public sealed record HookContext(
    string Event,
    string Subject,
    JsonElement Arguments = default,
    string? ResultHead = null,
    string? ConversationId = null,
    string? RunId = null,
    string? WorkspacePath = null,
    IReadOnlyDictionary<string, string>? Meta = null);

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-E01: a chat lifecycle hook.
/// <see cref="Matcher"/> is a regex applied to the event subject (tool name,
/// <c>mcp_{server}_{tool}</c>, <c>"run"</c>). When <see cref="BlockOnFailure"/>
/// is true a hook exception blocks the operation (fail-closed) instead of
/// failing open.
/// </summary>
public interface IChatHook
{
    /// <summary>Stable hook id used in verdict attribution and status notes.</summary>
    string Name { get; }

    /// <summary>The lifecycle event this hook listens to (<see cref="ChatHookEvents"/>).</summary>
    string Event { get; }

    /// <summary>Regex matched against the event subject; the hook only runs on a match.</summary>
    string Matcher { get; }

    /// <summary>Fail-closed switch on the hook record — a hook exception becomes a block.</summary>
    bool BlockOnFailure => false;

    Task<HookVerdict> RunAsync(HookContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Folded outcome of running every hook resolved for one event —
/// <see cref="Verdicts"/> holds the non-allow verdicts in registration order;
/// a block short-circuits the remaining hooks.
/// </summary>
public sealed record HookRunResult(HookVerdictKind Kind, IReadOnlyList<HookVerdict> Verdicts)
{
    public static HookRunResult Allow { get; } = new(HookVerdictKind.Allow, []);

    /// <summary>The block verdict that won, when <see cref="Kind"/> is <see cref="HookVerdictKind.Block"/>.</summary>
    public HookVerdict? Block => Verdicts.FirstOrDefault(v => v.Kind == HookVerdictKind.Block);

    /// <summary>Warn verdicts to surface as status notes.</summary>
    public IEnumerable<HookVerdict> Warnings => Verdicts.Where(v => v.Kind == HookVerdictKind.Warn);
}

/// <summary>
/// RF-E01: the hook engine contract — resolves registered hooks for an event
/// (matcher filter on the subject) and runs them to a folded verdict.
/// </summary>
public interface IChatHookRegistry
{
    /// <summary><c>Taskboard:Chat:Hooks:Enabled</c> — default on; false disables the engine.</summary>
    bool Enabled { get; }

    /// <summary>Hooks whose <see cref="IChatHook.Event"/> and <see cref="IChatHook.Matcher"/> match.</summary>
    IReadOnlyList<IChatHook> Resolve(string eventName, string subject);

    /// <summary>Runs the resolved hooks in registration order and folds their verdicts.</summary>
    Task<HookRunResult> RunAsync(HookContext context, CancellationToken cancellationToken);
}
