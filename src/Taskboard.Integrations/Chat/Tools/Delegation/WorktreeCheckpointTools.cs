using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Harness;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261006 RF-004: snapshots the run's worktree into a checkpoint commit.
/// </summary>
public sealed class WorktreeCheckpointTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "worktree_checkpoint";
    public string Description =>
        "Create a checkpoint commit of a delegation run's worktree (run_id = the "
        + "agent run id that owns the worktree, or the delegation task's run id).";
    public string ParametersJson => """
        {"type":"object","properties":{
          "run_id":{"type":"string","description":"Run id owning the worktree"},
          "label":{"type":"string","description":"Checkpoint label"}
        },"required":["run_id"]}
        """;

    public string CapabilityId => "agent:worktree_checkpoint";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var runId = ReadString(arguments, "run_id");
        if (string.IsNullOrWhiteSpace(runId))
        {
            return DelegationToolSupport.Error("run_id is required", "missing run id");
        }

        var label = ReadString(arguments, "label");

        await using var diScope = scopeFactory.CreateAsyncScope();
        var checkpoints = diScope.ServiceProvider.GetRequiredService<IWorkspaceCheckpointService>();
        try
        {
            var checkpoint = await checkpoints.CreateCheckpointAsync(runId, label, cancellationToken)
                .ConfigureAwait(false);
            return DelegationToolSupport.Ok(checkpoint);
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "checkpoint failed");
        }
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}

/// <summary>
/// SPEC-20261006 RF-004: lists a worktree's checkpoints; with
/// <c>restore_sha</c> hard-resets the worktree to it.
/// </summary>
public sealed class WorktreeCheckpointsTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "worktree_checkpoints";
    public string Description =>
        "List checkpoint commits of a run's worktree; pass restore_sha to roll "
        + "the worktree back to one (destructive to uncommitted work).";
    public string ParametersJson => """
        {"type":"object","properties":{
          "run_id":{"type":"string","description":"Run id owning the worktree"},
          "restore_sha":{"type":"string","description":"Checkpoint sha to restore"}
        },"required":["run_id"]}
        """;

    public string CapabilityId => "agent:worktree_checkpoints";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => true;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var runId = ReadString(arguments, "run_id");
        if (string.IsNullOrWhiteSpace(runId))
        {
            return DelegationToolSupport.Error("run_id is required", "missing run id");
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var checkpoints = diScope.ServiceProvider.GetRequiredService<IWorkspaceCheckpointService>();
        var restoreSha = ReadString(arguments, "restore_sha");
        try
        {
            if (!string.IsNullOrWhiteSpace(restoreSha))
            {
                var restored = await checkpoints
                    .RestoreCheckpointAsync(runId, restoreSha, cancellationToken)
                    .ConfigureAwait(false);
                return DelegationToolSupport.Ok(new { restored });
            }

            var list = await checkpoints.ListCheckpointsAsync(runId, ct: cancellationToken)
                .ConfigureAwait(false);
            return DelegationToolSupport.Ok(new { checkpoints = list });
        }
        catch (DomainException ex)
        {
            return DelegationToolSupport.Error(ex.Message, "checkpoint failed");
        }
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
