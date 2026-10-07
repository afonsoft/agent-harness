namespace Taskboard.ValueObjects;

/// <summary>
/// Permission presets of a provider-chat conversation
/// (SPEC-20261005-chat-tool-approval RF-006).
/// </summary>
public static class ChatPermissionPresets
{
    /// <summary>Read-only: mutating tool calls are refused pre-execution.</summary>
    public const string Chat = "chat";

    /// <summary>Default: mutating calls prompt for approval.</summary>
    public const string Ask = "ask";

    /// <summary>Never prompt — tools execute directly.</summary>
    public const string Full = "full";

    /// <summary>
    /// SPEC-20261013-chat-risk-approvals: per-call risk tiers — the static
    /// classifier allows <c>low</c> silently, <c>medium</c> with a notice,
    /// and asks on <c>high</c>. Tools flagged <c>RequiresConfirmation</c>
    /// keep their hard gate.
    /// </summary>
    public const string Auto = "auto";

    public static readonly IReadOnlyList<string> All = [Chat, Ask, Full, Auto];

    public static bool IsValid(string? preset) =>
        preset is Chat or Ask or Full or Auto;
}
