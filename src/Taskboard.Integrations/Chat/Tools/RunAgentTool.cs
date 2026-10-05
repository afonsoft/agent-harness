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
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory,
    ISecretRedactor redactor) : IChatTool
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

        var resolved = await ResolveAgentAsync(arguments, prompt, context, cancellationToken)
            .ConfigureAwait(false);
        if (resolved.Result is not null)
        {
            return resolved.Result;
        }

        var agent = resolved.Agent ?? throw new InvalidOperationException("resolution returned neither agent nor result");

        // B-07: unique id per delegation — two runs in the same conversation
        // must not share "chat:{id}", or wait=true could observe a previous
        // delegation's terminal state.
        var issueId = $"chat:{context.ConversationId ?? "unknown"}:{Guid.NewGuid().ToString("N")[..8]}";
        var request = BuildRequest(issueId, prompt, agent.Type, context);

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
        return await AwaitRunCompletionAsync(orchestration, context, issueId, agent.Name, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record AgentResolution(AgentInfo? Agent, ChatToolResult? Result);

    /// <summary>Picks the requested (or first available) builtin agent; when no
    /// builtin matches, a custom CLI def may still serve the run inline.</summary>
    private async Task<AgentResolution> ResolveAgentAsync(
        JsonElement arguments, string prompt, ChatToolContext context,
        CancellationToken cancellationToken)
    {
        var agents = await orchestration.GetAvailableAgentsAsync(cancellationToken).ConfigureAwait(false);
        // SPEC-20261003-ai-code-agent-chat: an omitted agent arg falls back to
        // the CLI picked in the conversation's Agent bar.
        var requested = arguments.TryGetProperty("agent", out var a) && a.ValueKind == JsonValueKind.String
            ? a.GetString()
            : context.DefaultAgentCli;
        var agent = requested is null
            ? agents.FirstOrDefault(x => x.Status == AgentStatus.Available)
            : agents.FirstOrDefault(x =>
                x.Status == AgentStatus.Available
                && (string.Equals(x.Name, requested, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Type.ToString(), requested, StringComparison.OrdinalIgnoreCase)));
        if (agent is not null)
        {
            return new(agent, null);
        }

        var customResult = await TryRunCustomDefAsync(
            requested, prompt, context, cancellationToken).ConfigureAwait(false);
        if (customResult is not null)
        {
            return new(null, customResult);
        }

        var eligible = string.Join(", ", agents.Where(x => x.Status == AgentStatus.Available).Select(x => x.Name));
        return new(null, new ChatToolResult(
            ErrorJson($"no eligible agent CLI{(eligible.Length > 0 ? $" (available: {eligible})" : null)}"),
            Refused: true, "no eligible agent"));
    }

    private static AgentExecutionRequest BuildRequest(
        string issueId, string prompt, AgentType agentType, ChatToolContext context) =>
        new(
            IssueId: issueId,
            IssueNumber: 0,
            RepositoryFullName: string.Empty,
            RepoPath: context.WorkspacePath,
            Branch: null,
            Scope: null,
            Instructions: $"(delegated from chat conversation {context.ConversationId})\n\n{prompt}",
            AgentType: agentType,
            ResolvedModelName: string.IsNullOrWhiteSpace(context.DefaultAgentModel)
                ? null
                : context.DefaultAgentModel,
            OmitModelFlag: string.IsNullOrWhiteSpace(context.DefaultAgentModel));

    // SPEC-20261004 RF-007: a custom CLI definition is also a valid agent —
    // run it inline via its args template (defs aren't AgentTypes, so they
    // can't take the orchestration queue). Null when no matching def exists.
    private async Task<ChatToolResult?> TryRunCustomDefAsync(
        string? requested, string prompt, ChatToolContext context, CancellationToken cancellationToken)
    {
        var def = await CustomCliRunner.FindAsync(scopeFactory, requested, cancellationToken)
            .ConfigureAwait(false);
        if (def is null)
        {
            return null;
        }

        var resolved = CustomCliRunner.ResolveExecutable(def.Executable);
        if (resolved is null)
        {
            return new ChatToolResult(
                ErrorJson($"custom cli '{def.DisplayName}' is not installed on the host"),
                Refused: true, "custom cli not installed");
        }

        var (argv, stdin) = CustomCliRunner.BuildInvocation(def, prompt, context.DefaultAgentModel);
        context.Activity?.Report("running_agent", def.DisplayName);
        return await CustomCliRunner.ExecAsync(
            resolved, argv, stdin, context.WorkspacePath, redactor,
            cancellationToken, def.DisplayName).ConfigureAwait(false);
    }

    private async Task<ChatToolResult> AwaitRunCompletionAsync(
        IAgentOrchestrationService orchestration, ChatToolContext context,
        string issueId, string agentName, CancellationToken cancellationToken)
    {
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
                    agent = agentName,
                    tail,
                }));
            }

            context.Activity?.Report("running_agent", $"{agentName} ({latest?.State.ToString().ToLowerInvariant() ?? "queued"})");
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }

        return new ChatToolResult(JsonSerializer.Serialize(new
        {
            issue_id = issueId,
            status = "still_running",
            agent = agentName,
            note = $"run exceeds {timeoutSeconds}s — track it in the runs panel",
        }));
    }

    private int ParseInt(string key, int fallback) =>
        int.TryParse(configuration[key], out var v) && v > 0 ? v : fallback;

    private static string ErrorJson(string message) => JsonSerializer.Serialize(new { error = message });
}
