using Taskboard.Agents;
using Taskboard.Application.Contracts.AiChat;
using Taskboard.Integrations.Workspace;

namespace Taskboard.Server.Services;

/// <summary>
/// One-shot CLI execution for assistant threads (SPEC-20260921-ai-chat-cli-backend):
/// reuses <see cref="IAgentAcpClient"/> with the workspace root as cwd (no
/// workdir binding in assistant mode). The session path
/// (<see cref="AgentSessionManager"/>) stays exclusive to agent-mode threads —
/// <c>session/prompt</c> is fire-and-forget and cannot drive the run lifecycle.
/// </summary>
public sealed class CliChatRunner(IAgentAcpClient acpClient, WorkspaceService workspace) : ICliChatRunner
{
    public Task<AgentExecutionResult> RunAsync(
        string threadId,
        AgentType agentType,
        string? modelName,
        string prompt,
        IProgress<AgentLogMessage> progress,
        CancellationToken cancellationToken = default)
    {
        var request = new AgentExecutionRequest(
            IssueId: threadId,
            IssueNumber: 0,
            RepositoryFullName: "local",
            RepoPath: workspace.EnsureRoot(),
            Branch: null,
            Scope: null,
            Instructions: prompt,
            AgentType: agentType,
            ResolvedModelName: modelName);

        return acpClient.ExecuteAsync(request, progress, cancellationToken);
    }
}
