namespace Taskboard.Application.Contracts.AiChat;

/// <summary>
/// Derives a thread title from the first user prompt
/// (SPEC-20260922-ai-chat-command-bar RF-007): whitespace-normalized,
/// truncated at ~60 chars on a word boundary with an ellipsis.
/// </summary>
public static class AiChatThreadTitle
{
    public const int MaxLength = 60;

    public static string Derive(string prompt)
    {
        var normalized = string.Join(' ', prompt.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length == 0)
        {
            return "New conversation";
        }

        if (normalized.Length <= MaxLength)
        {
            return normalized;
        }

        var cut = normalized[..MaxLength];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > MaxLength / 3)
        {
            cut = cut[..lastSpace];
        }

        return cut.TrimEnd() + "…";
    }
}
