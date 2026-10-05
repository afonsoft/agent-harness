using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.GitHub;
using static Taskboard.Integrations.Chat.Tools.Board.BoardToolSupport;

namespace Taskboard.Integrations.Chat.Tools.Board;

/// <summary>
/// SPEC-20261004-chat-board-tools RF-009: chained "card → agent" tool — seeds a
/// delegation task from the issue (same flow as POST
/// local/delegation/tasks/from-issue), gives it an isolated worktree and moves
/// the card to in-progress. The dispatcher picks the pending task up.
/// </summary>
public sealed class BoardDelegateIssueTool(
    IGitHubService github,
    IServiceScopeFactory scopeFactory) : IChatTool
{
    public string Name => "board_delegate_issue";
    public string Description =>
        "Delegate a board card (GitHub issue) to an agent CLI: creates a delegated task with "
        + "a git worktree (dispatcher runs it asynchronously) and moves the card to "
        + "in-progress. Use cli to pick the agent; prompt to override the default "
        + "'issue title + body' instructions.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "repository":{"type":"string","description":"GitHub repository 'owner/name'"},
          "number":{"type":"integer","description":"Issue number"},
          "cli":{"type":"string","description":"Target agent CLI (default: conversation's picked CLI, else opencode)"},
          "prompt":{"type":"string","description":"Instructions override (default: issue title + body)"}
        },"required":["repository","number"]}
        """;

    public string CapabilityId => "agent:board_delegate_issue";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => true;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var repo = RepoOf(arguments, out var error);
        if (error is not null)
        {
            return Task.FromResult(error);
        }

        if (NumberOf(arguments) is not { } number)
        {
            return Task.FromResult(Error("number is required", "missing-number"));
        }

        return GuardAsync(async () =>
        {
            var issue = await github.GetIssueAsync(repo!, number, cancellationToken).ConfigureAwait(false);
            if (issue is null)
            {
                return Error($"issue #{number} not found", "not-found");
            }

            var cli = ReadString(arguments, "cli") ?? context.DefaultAgentCli;
            if (string.IsNullOrWhiteSpace(cli))
            {
                cli = "opencode";
            }

            var prompt = ReadString(arguments, "prompt");
            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt = $"GitHub issue {repo}#{issue.Number}: {issue.Title}\n\n"
                    + (string.IsNullOrWhiteSpace(issue.Body) ? "(no body)" : issue.Body);
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var provisioning = scope.ServiceProvider.GetRequiredService<IRepositoryProvisioningService>();
            var delegation = scope.ServiceProvider.GetRequiredService<IDelegationService>();
            var isolation = scope.ServiceProvider.GetRequiredService<IWorkspaceIsolationService>();

            RepositoryCloneResult clone;
            try
            {
                clone = await provisioning.EnsureCloneAsync(repo!, cancellationToken).ConfigureAwait(false);
            }
            catch (DomainException ex)
            {
                return Error(ex.Message, "clone-failed");
            }

            var task = await delegation.CreateTaskAsync(
                new CreateDelegationTaskRequest(
                    prompt, cli.Trim(),
                    Taskboard.Integrations.Chat.Tools.Delegation.DelegationToolSupport.ScopeOf(context),
                    clone.Path,
                    DependsOn: null, RetryOf: null, FanoutGroupId: null,
                    UseWorktree: true, clone.Path, BaseCommitSha: null),
                cancellationToken).ConfigureAwait(false);

            var session = await isolation.CreateWorktreeAsync(
                task.Id, clone.Path, "main",
                $"issue-{issue.Number}-{task.Id[^Math.Min(8, task.Id.Length)..]}",
                retainOnFailure: false, cancellationToken).ConfigureAwait(false);
            task = (await delegation.AttachWorktreeAsync(task.Id, session.RunId, cancellationToken)
                .ConfigureAwait(false)) ?? task;

            await github.UpdateIssueColumnAsync(
                repo!, issue.Number,
                issue.Column.HasLabel() ? issue.Column : null,
                GitHubBoardColumn.InProgress, cancellationToken).ConfigureAwait(false);

            return Ok(new
            {
                taskId = task.Id,
                issueNumber = issue.Number,
                url = issue.HtmlUrl,
                column = GitHubBoardColumn.InProgress.ToDisplayName(),
                cli = cli.Trim(),
                repositoryPath = clone.Path
            });
        });
    }
}
