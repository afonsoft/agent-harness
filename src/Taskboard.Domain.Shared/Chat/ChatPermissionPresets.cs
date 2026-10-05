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

    public static readonly IReadOnlyList<string> All = [Chat, Ask, Full];

    public static bool IsValid(string? preset) =>
        preset is Chat or Ask or Full;
}
