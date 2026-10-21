using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Dtos;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261020-v2 RF-004: blocking worker → coordinator question. Waits for
/// <c>agent_reply</c> up to <c>timeout_ms</c>; on timeout the question stays
/// resumable via <c>resume</c> (re-blocks the same message, no duplicate).
/// </summary>
public sealed class AgentAskTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "agent_ask";
    public string Description =>
        "Ask a blocking question to the coordinator/human. Returns the answer "
        + "when replied, or a resumable message id on timeout — resume with "
        + "resume=<messageId> to keep waiting on the same question.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "question":{"type":"string","description":"Question body (required unless resuming)"},
          "options":{"type":"array","items":{"type":"string"},"description":"Suggested answers"},
          "timeout_ms":{"type":"integer","description":"Wait timeout in ms (default 60000, max 300000)"},
          "resume":{"type":"string","description":"Message id of a previous question to keep waiting on"},
          "to":{"type":"string","description":"Recipient address (default @all)"}
        }}
        """;

    public string CapabilityId => "agent:ask";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var scope = DelegationToolSupport.ScopeOf(context);
        var resume = ReadString(arguments, "resume");
        var question = ReadString(arguments, "question");
        if (resume is null && string.IsNullOrWhiteSpace(question))
        {
            return DelegationToolSupport.Error("question is required", "empty question");
        }

        var timeout = arguments.TryGetProperty("timeout_ms", out var t) && t.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromMilliseconds(Math.Clamp(t.GetInt32(), 100, 300_000))
            : (TimeSpan?)null;

        var options = new List<string>();
        if (arguments.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array)
        {
            options.AddRange(o.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!));
        }

        var from = string.IsNullOrWhiteSpace(context.DefaultAgentCli)
            ? "assistant"
            : context.DefaultAgentCli!;

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();

        try
        {
            var result = await service.AskQuestionAsync(
                new AskQuestionRequest(
                    scope, from, question ?? string.Empty,
                    options.Count == 0 ? null : options,
                    timeout, resume, ReadString(arguments, "to")),
                cancellationToken).ConfigureAwait(false);

            return DelegationToolSupport.Ok(new
            {
                messageId = result.MessageId,
                questionId = result.Id,
                status = result.Status.ToString().ToLowerInvariant(),
                result.Answer,
            });
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "ask refused");
        }
    }

    internal static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}

/// <summary>
/// SPEC-20261020-v2 RF-004: coordinator → worker answer — resolves the pending
/// question bound to the message id.
/// </summary>
public sealed class AgentReplyTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "agent_reply";
    public string Description =>
        "Answer a pending worker question: agent_reply <message_id> <answer>. "
        + "Posts an 'answer' message back to the asker; out-of-cycle replies "
        + "fail with question_not_pending.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "message_id":{"type":"string","description":"Message id of the pending question"},
          "answer":{"type":"string","description":"Answer text (or one of the question options)"}
        },"required":["message_id","answer"]}
        """;

    public string CapabilityId => "agent:reply";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var scope = DelegationToolSupport.ScopeOf(context);
        var messageId = AgentAskTool.ReadString(arguments, "message_id");
        var answer = AgentAskTool.ReadString(arguments, "answer");
        if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(answer))
        {
            return DelegationToolSupport.Error("message_id and answer are required", "missing args");
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var service = diScope.ServiceProvider.GetRequiredService<IDelegationService>();

        try
        {
            var result = await service.ReplyQuestionAsync(
                scope, messageId, answer, context.DefaultAgentCli, cancellationToken)
                .ConfigureAwait(false);
            return DelegationToolSupport.Ok(new
            {
                questionId = result.Id,
                status = result.Status.ToString().ToLowerInvariant(),
                result.Answer,
            });
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "reply refused");
        }
    }
}
