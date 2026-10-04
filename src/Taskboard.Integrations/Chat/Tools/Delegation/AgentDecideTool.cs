using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261006 RF-005: the decision gate — the assistant posts a system
/// <c>decision</c> message that blocks scope progress until a human answers
/// via mailbox (surfaces on the dashboard's Needs You column).
/// </summary>
public sealed class AgentDecideTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "agent_decide";
    public string Description =>
        "Ask a blocking decision question to the humans watching this conversation. "
        + "Posts a decision message to the mailbox; answers arrive via agent_inbox.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "question":{"type":"string","description":"The decision the human must make"},
          "context":{"type":"string","description":"Optional background for the decision"}
        },"required":["question"]}
        """;

    public string CapabilityId => "agent:decide";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetProperty("question", out var qEl)
            || string.IsNullOrWhiteSpace(qEl.GetString()))
        {
            return DelegationToolSupport.Error("question is required", "missing question");
        }

        var question = qEl.GetString()!;
        if (arguments.TryGetProperty("context", out var ctxEl)
            && !string.IsNullOrWhiteSpace(ctxEl.GetString()))
        {
            question = $"{question}\n\ncontext: {ctxEl.GetString()}";
        }

        var scope = DelegationToolSupport.ScopeOf(context);
        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();
        var from = string.IsNullOrWhiteSpace(context.DefaultAgentCli)
            ? "assistant"
            : context.DefaultAgentCli!;

        var message = await service.PostDecisionAsync(scope, from, question, cancellationToken)
            .ConfigureAwait(false);

        return DelegationToolSupport.Ok(new
        {
            decisionId = message.Id,
            kind = AgentMailboxKinds.Decision,
            scope,
            note = "decision posted — answers arrive as mailbox 'text' messages",
        });
    }
}
