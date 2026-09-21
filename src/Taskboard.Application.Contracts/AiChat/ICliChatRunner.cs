using Taskboard.Agents;

namespace Taskboard.Application.Contracts.AiChat;

/// <summary>
/// Executes one assistant turn through an installed agent CLI (one-shot, the
/// thread transcript as instructions). Implementation lives in the Server
/// layer because it needs the workspace root and process spawning
/// (SPEC-20260921-ai-chat-cli-backend RF-004).
/// </summary>
public interface ICliChatRunner
{
    /// <summary>
    /// Runs the CLI once for the thread. Output lines stream through
    /// <paramref name="progress"/>; the process exit code decides success.
    /// </summary>
    Task<AgentExecutionResult> RunAsync(
        string threadId,
        AgentType agentType,
        string? modelName,
        string prompt,
        IProgress<AgentLogMessage> progress,
        CancellationToken cancellationToken = default);
}
