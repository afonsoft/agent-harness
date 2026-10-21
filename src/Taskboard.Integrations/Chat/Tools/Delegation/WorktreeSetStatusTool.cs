using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Harness;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261020-worktree-shared-context RF-007: annotates a worktree card —
/// <c>comment</c> (≤500c) + <c>status</c> (clean|dirty|conflict|unknown).
/// Null/unknown status derives via <c>git status --porcelain</c> on demand.
/// </summary>
public sealed class WorktreeSetStatusTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "worktree_set_status";
    public string Description =>
        "Set a worktree card comment/status (worktreePath required; status "
        + "clean|dirty|conflict|unknown — omit to derive from git status).";
    public string ParametersJson => """
        {"type":"object","properties":{
          "worktreePath":{"type":"string","description":"Absolute worktree path"},
          "comment":{"type":"string","description":"Status comment, ≤500 chars"},
          "status":{"type":"string","description":"clean|dirty|conflict|unknown"},
          "taskId":{"type":"string","description":"Owning delegation task id"}
        },"required":["worktreePath"]}
        """;

    public string CapabilityId => "agent:worktree_set_status";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var worktreePath = ReadString(arguments, "worktreePath");
        if (string.IsNullOrWhiteSpace(worktreePath))
        {
            return DelegationToolSupport.Error("worktreePath is required", "missing path");
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var cards = diScope.ServiceProvider.GetRequiredService<IWorktreeCardService>();
        try
        {
            var card = await cards.SetCardAsync(
                worktreePath,
                ReadString(arguments, "comment"),
                ReadString(arguments, "status"),
                ReadString(arguments, "taskId"),
                cancellationToken).ConfigureAwait(false);
            return DelegationToolSupport.Ok(card);
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "card write failed");
        }
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
