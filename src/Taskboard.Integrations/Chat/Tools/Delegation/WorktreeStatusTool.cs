using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Harness;

namespace Taskboard.Integrations.Chat.Tools.Delegation;

/// <summary>
/// SPEC-20261021-worktree-setup-scripts RF-008: read a worktree's full status —
/// git cleanliness probe + card annotation + setup lifecycle
/// (<c>setupState</c>/<c>setupLogTail</c>) + shared paths applied +
/// <c>defaultTabs:</c> declared in the repo's yaml. Address by
/// <c>runId</c> (worktree session) or <c>worktreePath</c> (card).
/// </summary>
public sealed class WorktreeStatusTool(IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "worktree_status";
    public string Description =>
        "Read a worktree's status: workspace cleanliness (derived from git status), "
        + "card comment, setup lifecycle (setupState + log tail), shared paths "
        + "applied and declared defaultTabs. Pass runId or worktreePath.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "runId":{"type":"string","description":"Worktree session run id"},
          "worktreePath":{"type":"string","description":"Absolute worktree path (alternative to runId)"}
        }}
        """;

    public string CapabilityId => "agent:worktree_status";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => false;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var runId = ReadString(arguments, "runId");
        var worktreePath = ReadString(arguments, "worktreePath");
        if (string.IsNullOrWhiteSpace(runId) && string.IsNullOrWhiteSpace(worktreePath))
        {
            return DelegationToolSupport.Error(
                "runId or worktreePath is required", "missing identifier");
        }

        await using var diScope = scopeFactory.CreateAsyncScope();
        var sessions = diScope.ServiceProvider.GetRequiredService<IWorktreeSessionRepository>();
        var cards = diScope.ServiceProvider.GetRequiredService<IWorktreeCardService>();
        var probe = diScope.ServiceProvider.GetRequiredService<IWorktreeStatusProbe>();
        var yamlReader = diScope.ServiceProvider.GetService<IWorktreeYamlReader>();

        var session = runId is not null
            ? await sessions.GetByRunIdAsync(runId, cancellationToken).ConfigureAwait(false)
            : null;
        var path = session?.Path ?? worktreePath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return DelegationToolSupport.Error(
                "worktree not found (no session for runId / path missing)", "not found");
        }

        var repoPath = session?.RepositoryPath
            ?? await probe.ResolveRepoPathAsync(path, cancellationToken).ConfigureAwait(false);
        var card = await cards.GetAsync(path, cancellationToken).ConfigureAwait(false);
        var derived = await probe.ProbeAsync(path, cancellationToken).ConfigureAwait(false);
        var tabs = repoPath is not null && yamlReader is not null
            ? yamlReader.Read(repoPath).DefaultTabs
            : null;

        return DelegationToolSupport.Ok(new
        {
            runId = session?.RunId ?? runId,
            worktreePath = path,
            repoPath,
            branch = session?.Branch,
            sessionStatus = session?.Status,
            workspaceStatus = card?.WorkspaceStatus.ToString()
                ?? derived.ToString(),
            comment = card?.Comment,
            sharedPathsApplied = session?.SharedPathsApplied,
            setupState = card?.SetupState ?? session?.SetupState,
            setupExitCode = card?.SetupExitCode,
            setupLogTail = card?.SetupLogTail ?? session?.SetupLogTail,
            defaultTabs = tabs is { Count: > 0 }
                ? tabs.Select(t => new { name = t.Name, command = t.Command }).ToArray()
                : null,
        });
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
