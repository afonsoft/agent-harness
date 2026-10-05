namespace Taskboard.ValueObjects;

/// <summary>
/// Plan-mode state of a provider-chat conversation
/// (SPEC-20261005-chat-plan-mode RF-001).
/// </summary>
public static class ChatPlanModes
{
    public const string On = "on";
    public const string Off = "off";

    public static bool IsValid(string? mode) =>
        mode is On or Off;
}
