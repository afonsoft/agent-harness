using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Harness;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261021-worktree-setup-scripts P3: manually re-runs the worktree's
/// declared <c>setup:</c> commands — resets the card to
/// <c>running</c> → terminal and overwrites the session's setup tail
/// (previous log is replaced). Same code path as
/// <c>POST /api/harness/worktrees/{runId}/setup:rerun</c>.
/// </summary>
public sealed class WorktreeSetupRerunTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "worktree_setup";
    public string Description =>
        "Re-run the worktree's declared .harness/worktree.yaml setup commands "
        + "(runId required). Card flips running → ok|failed; previous log is replaced.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "runId":{"type":"string","description":"Worktree session run id"}
        },"required":["runId"]}
        """;

    public string CapabilityId => "agent:worktree_setup";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => true;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var runId = ReadString(arguments, "runId");
        if (string.IsNullOrWhiteSpace(runId))
        {
            return DelegationToolSupport.Error("runId is required", "missing runId");
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var sessions = diScope.ServiceProvider.GetRequiredService<IWorktreeSessionRepository>();
        var yamlReader = diScope.ServiceProvider.GetRequiredService<IWorktreeYamlReader>();
        var setupRunner = diScope.ServiceProvider.GetRequiredService<IWorktreeSetupRunner>();

        var session = await sessions.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        if (session is null || !Directory.Exists(session.Path))
        {
            return DelegationToolSupport.Error(
                "run has no live worktree", "no worktree");
        }

        var doc = yamlReader.Read(session.RepositoryPath);
        var commands = yamlReader.EffectiveSetupCommands(session.RepositoryPath, doc);
        if (commands.Count == 0)
        {
            return DelegationToolSupport.Error(
                "no setup commands declared", "no setup");
        }

        var result = await setupRunner.RunSetupAsync(
            session.RepositoryPath, session.Path, commands, cancellationToken).ConfigureAwait(false);
        var updated = await sessions.SetSetupAsync(
            runId, result.State,
            Integrations.Harness.WorktreeSetupRunner.PreambleTail(result.LogTail),
            cancellationToken).ConfigureAwait(false);

        return DelegationToolSupport.Ok(new
        {
            runId,
            setupState = result.State,
            exitCode = result.ExitCode,
            session = updated,
        });
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
