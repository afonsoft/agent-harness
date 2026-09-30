using System.Text.Json;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Delegates work from provider chat to a real agent CLI
/// (SPEC-20261001-chat-agent-delegation FR-001): enqueues an
/// <see cref="AgentExecutionRequest"/> bound to the conversation
/// (<c>chat:{conversationId}</c>) through <see cref="IAgentOrchestrationService"/>,
/// optionally waiting for the terminal state.
/// </summary>
public sealed class RunAgentTool(
    IAgentOrchestrationService orchestration,
    Microsoft.Extensions.Configuration.IConfiguration configuration) : IChatTool
{
    public string Name => "run_agent";
    public string Description =>
        "Delegate a coding task to a full agent CLI (Codex, Claude, etc.) running "
        + "against the workspace. Use for real implementation work. Returns a run id "
        + "immediately unless wait=true.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "prompt":{"type":"string","description":"Instructions for the agent"},
          "agent":{"type":"string","description":"Agent CLI name (e.g. 'codex'); omit for the first eligible"},
          "wait":{"type":"boolean","description":"Wait for the run to finish (default false, max 120s)"}
        },"required":["prompt"]}
        """;

    public string CapabilityId => "agent:run";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => true;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        // FR-003: delegation is a leaf — refused inside a sub-agent turn.
        if (context.DelegationDepth > 0)
        {
            return new ChatToolResult(ErrorJson("delegation not allowed inside a sub-agent"), Refused: true, "recursion blocked");
        }

        var prompt = arguments.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new ChatToolResult(ErrorJson("prompt is required"), Refused: true, "empty prompt");
        }

        var wait = arguments.TryGetProperty("wait", out var w) && w.ValueKind is JsonValueKind.True;

        var agents = await orchestration.GetAvailableAgentsAsync(cancellationToken).ConfigureAwait(false);
        var requested = arguments.TryGetProperty("agent", out var a) && a.ValueKind == JsonValueKind.String
            ? a.GetString()
            : null;
        var agent = requested is null
            ? agents.FirstOrDefault(x => x.Status == AgentStatus.Available)
            : agents.FirstOrDefault(x =>
                x.Status == AgentStatus.Available
                && (string.Equals(x.Name, requested, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Type.ToString(), requested, StringComparison.OrdinalIgnoreCase)));
        if (agent is null)
        {
            var eligible = string.Join(", ", agents.Where(x => x.Status == AgentStatus.Available).Select(x => x.Name));
            return new ChatToolResult(
                ErrorJson($"no eligible agent CLI{(eligible.Length > 0 ? $" (available: {eligible})" : null)}"),
                Refused: true, "no eligible agent");
        }

        var issueId = $"chat:{context.ConversationId ?? "unknown"}";
        var repoPath = context.WorkspacePath;
        var request = new AgentExecutionRequest(
            IssueId: issueId,
            IssueNumber: 0,
            RepositoryFullName: string.Empty,
            RepoPath: repoPath,
            Branch: null,
            Scope: null,
            Instructions: $"(delegated from chat conversation {context.ConversationId})\n\n{prompt}",
            AgentType: agent.Type);

        context.Activity?.Report("running_agent", agent.Name);

        if (!await orchestration.EnqueueAsync(request, cancellationToken).ConfigureAwait(false))
        {
            return new ChatToolResult(ErrorJson("agent rejected the run (disabled or unauthenticated)"), Refused: true, "enqueue refused");
        }

        if (!wait)
        {
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                issue_id = issueId,
                status = "queued",
                agent = agent.Name,
                link = "/ai-chat",
            }));
        }

        // FR-001 wait path — poll the run state until terminal or timeout.
        var timeoutSeconds = ParseInt("Taskboard:Chat:Delegation:WaitTimeoutSeconds", 120);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runs = await orchestration.GetRunsAsync(issueId, 1, cancellationToken).ConfigureAwait(false);
            var latest = runs.FirstOrDefault();
            if (latest?.State is AgentRunState.Succeeded or AgentRunState.Failed
                or AgentRunState.Canceled or AgentRunState.BudgetExceeded)
            {
                var logs = await orchestration.GetLogsAsync(issueId, cancellationToken).ConfigureAwait(false);
                var tail = string.Join('\n', logs.TakeLast(20).Select(l => l.Content));
                return new ChatToolResult(JsonSerializer.Serialize(new
                {
                    issue_id = issueId,
                    run_id = latest.Id,
                    status = latest.State.ToString().ToLowerInvariant(),
                    agent = agent.Name,
                    tail,
                }));
            }

            context.Activity?.Report("running_agent", $"{agent.Name} ({latest?.State.ToString().ToLowerInvariant() ?? "queued"})");
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }

        return new ChatToolResult(JsonSerializer.Serialize(new
        {
            issue_id = issueId,
            status = "still_running",
            agent = agent.Name,
            note = $"run exceeds {timeoutSeconds}s — track it in the runs panel",
        }));
    }

    private int ParseInt(string key, int fallback) =>
        int.TryParse(configuration[key], out var v) && v > 0 ? v : fallback;

    private static string ErrorJson(string message) => JsonSerializer.Serialize(new { error = message });
}
