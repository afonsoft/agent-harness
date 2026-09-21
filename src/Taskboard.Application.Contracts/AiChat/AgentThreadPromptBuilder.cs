using System.Text;
using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.AiChat;

/// <summary>
/// Builds the agent-CLI instructions prompt for a "Run agent" sub-task from an
/// AI chat thread (SPEC-20260918-ai-chat-threads RF-003): a system-style header
/// plus the last messages of the conversation, capped so a long thread still
/// fits a single CLI invocation.
/// </summary>
public static class AgentThreadPromptBuilder
{
    public const int DefaultMaxMessages = 20;
    public const int DefaultMaxChars = 8000;

    /// <summary>
    /// Builds the prompt. Keeps the header and the closing instruction, then as
    /// many of the most recent events as fit inside <paramref name="maxChars"/>
    /// — newest first, so the latest user message is never dropped.
    /// </summary>
    public static string Build(
        string threadTitle,
        string repositoryFullName,
        IReadOnlyList<AiChatEventDto> events,
        int maxMessages = DefaultMaxMessages,
        int maxChars = DefaultMaxChars)
    {
        var header = "You are an autonomous coding agent working on the repository '"
            + repositoryFullName
            + "'.\nThe following is a conversation between the user and an AI assistant "
            + "in the Harness taskboard, thread '"
            + threadTitle
            + "'.\n\n<conversation>\n";

        var closing = "</conversation>\n\n"
            + "Continue this work in the repository: act on the latest user intent, "
            + "apply the needed changes and report what you did.";

        return Assemble(header, events, closing, maxMessages, maxChars);
    }

    /// <summary>
    /// Builds the one-shot prompt for an assistant-mode thread executed through
    /// an agent CLI (SPEC-20260921-ai-chat-cli-backend RF-004): chat framing —
    /// the CLI answers the latest user message, no repository work is implied.
    /// </summary>
    public static string BuildAssistantPrompt(
        string threadTitle,
        IReadOnlyList<AiChatEventDto> events,
        int maxMessages = DefaultMaxMessages,
        int maxChars = DefaultMaxChars)
    {
        var header = "You are the Harness AI assistant in chat thread '"
            + threadTitle
            + "'. This is a conversational assistant exchange, not a coding task.\n\n<conversation>\n";

        var closing = "</conversation>\n\n"
            + "Reply concisely to the latest user message. Plain text or markdown; do not modify files.";

        return Assemble(header, events, closing, maxMessages, maxChars);
    }

    // Fills the conversation budget newest-first: the closing instruction and
    // the latest user message always survive — older events are dropped before
    // the newest one, and an oversized newest message is trimmed at the head
    // (keeping its tail, which carries the actual question).
    private static string Assemble(
        string header,
        IReadOnlyList<AiChatEventDto> events,
        string closing,
        int maxMessages,
        int maxChars)
    {
        var recent = events.Count <= maxMessages
            ? events
            : events.Skip(events.Count - maxMessages).ToList();

        var kept = new List<string>(recent.Count);
        var budget = Math.Max(0, maxChars - header.Length - closing.Length);
        for (var i = recent.Count - 1; i >= 0 && budget > 0; i--)
        {
            var ev = recent[i];
            var role = ev.Role switch
            {
                "user" => "user",
                "assistant" => "assistant",
                _ => "system"
            };
            var line = role + ": " + ev.Content.Trim() + '\n';
            if (line.Length > budget)
            {
                if (kept.Count == 0)
                {
                    kept.Add(line[^budget..]);
                }
                break;
            }
            kept.Add(line);
            budget -= line.Length;
        }
        kept.Reverse();

        return new StringBuilder(maxChars)
            .Append(header)
            .Append(string.Concat(kept))
            .Append(closing)
            .ToString();
    }
}
