namespace Taskboard.Application.Contracts.AiChat;

/// <summary>
/// Resolves the default /ai-chat mode (SPEC-20261001-chat-default-mode).
/// <c>Taskboard:AiChat:DefaultMode</c> = <c>auto|chat|agent</c>.
/// <c>auto</c> prefers the richest chat surface configured: provider chat
/// first, then a chat-capable agent CLI; falls back to Agent mode with a
/// <see cref="FellBack"/> flag so the UI can show a hint.
/// </summary>
public static class DefaultModePolicy
{
    public sealed record Resolution(string Mode, bool FellBack);

    /// <param name="configured">Raw config value — invalid values behave as <c>auto</c>.</param>
    /// <param name="providerChatAvailable">At least one enabled chat provider.</param>
    /// <param name="cliChatAvailable">At least one selectable chat-capable CLI.</param>
    public static Resolution Resolve(string? configured, bool providerChatAvailable, bool cliChatAvailable)
    {
        var mode = configured?.Trim().ToLowerInvariant() switch
        {
            "chat" or "agent" => configured.Trim().ToLowerInvariant(),
            _ => "auto",
        };

        var chatAvailable = providerChatAvailable || cliChatAvailable;
        return mode switch
        {
            "agent" => new Resolution("agent", FellBack: false),
            "chat" when chatAvailable => new Resolution(
                providerChatAvailable ? "provider" : "assistant", FellBack: false),
            "chat" => new Resolution("agent", FellBack: true),
            _ when chatAvailable => new Resolution(
                providerChatAvailable ? "provider" : "assistant", FellBack: false),
            _ => new Resolution("agent", FellBack: true),
        };
    }
}
