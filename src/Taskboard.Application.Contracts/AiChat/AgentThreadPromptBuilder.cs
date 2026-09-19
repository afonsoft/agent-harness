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
    /// Builds the prompt. Keeps the header, then as many of the most recent
    /// events as fit inside <paramref name="maxChars"/>; the returned string is
    /// hard-truncated at <paramref name="maxChars"/>.
    /// </summary>
    public static string Build(
        string threadTitle,
        string repositoryFullName,
        IReadOnlyList<AiChatEventDto> events,
        int maxMessages = DefaultMaxMessages,
        int maxChars = DefaultMaxChars)
    {
        var builder = new StringBuilder();
        builder.Append("You are an autonomous coding agent working on the repository '")
            .Append(repositoryFullName)
            .Append("'.\nThe following is a conversation between the user and an AI assistant ")
            .Append("in the Harness taskboard, thread '")
            .Append(threadTitle)
            .Append("'.\n\n<conversation>\n");

        var recent = events.Count <= maxMessages
            ? events
            : events.Skip(events.Count - maxMessages).ToList();

        foreach (var ev in recent)
        {
            var role = ev.Role switch
            {
                "user" => "user",
                "assistant" => "assistant",
                _ => "system"
            };
            builder.Append(role).Append(": ").Append(ev.Content.Trim()).Append('\n');
        }

        builder.Append("</conversation>\n\n")
            .Append("Continue this work in the repository: act on the latest user intent, ")
            .Append("apply the needed changes and report what you did.");

        var prompt = builder.ToString();
        return prompt.Length <= maxChars ? prompt : prompt[..maxChars];
    }
}
