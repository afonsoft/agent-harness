using Taskboard.Dtos;

namespace Taskboard.Application.Contracts.AiChat;

/// <summary>
/// Models reported by the live ACP session of a thread — the peer advertises
/// them as <c>configOptions</c> entries with <c>category:"model"</c>
/// (SPEC-20260921-ai-code-thread-config RF-005).
/// </summary>
public interface IAgentSessionModelCatalog
{
    /// <summary>
    /// Agent-reported models for the thread's active session — empty when no
    /// session is running or the peer does not advertise model options.
    /// </summary>
    IReadOnlyList<AiChatModelDto> GetModels(string threadId);
}
