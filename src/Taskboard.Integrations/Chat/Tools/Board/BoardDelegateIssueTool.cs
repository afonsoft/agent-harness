using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.GitHub;
using Taskboard.Harness;
using Taskboard.Integrations.Chat.Tools.Delegation;
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
        + "'issue title + body' instructions; prompt prompt must be a self-contained brief: goal + context + spec ref (SPEC-... RF/AC) + acceptance + report format.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "repository":{"type":"string","description":"GitHub repository 'owner/name'"},
          "number":{"type":"integer","description":"Issue number"},
          "cli":{"type":"string","description":"Target agent CLI (default: conversation's picked CLI, else opencode)"},
          "prompt":{"type":"string","description":"Instructions override (default: issue title + body)"},
          "autonomy":{"type":"string","enum":["plan","edit","full"],"description":"Permission envelope (default edit); full requires the worktree + server opt-in"}
        },"required":["repository","number"]}
        """;

    public string CapabilityId => "agent:board_delegate_issue";
    public ChatCapabilityKind Kind => ChatCapabilityKind.AgentDelegation;
    public bool RequiresConfirmation => true;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken) =>
        GuardAsync(async () =>
        {
            var (repo, _, issue, error) = await RequireIssueAsync(
                github, arguments, cancellationToken).ConfigureAwait(false);
            if (error is not null || repo is null || issue is null)
            {
                return error ?? Error("issue not found", "not-found");
            }

            var cli = ReadString(arguments, "cli") ?? context.DefaultAgentCli;
            if (string.IsNullOrWhiteSpace(cli))
            {
                cli = "opencode";
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var provisioning = scope.ServiceProvider.GetRequiredService<IRepositoryProvisioningService>();
            var delegation = scope.ServiceProvider.GetRequiredService<IDelegationService>();
            var isolation = scope.ServiceProvider.GetRequiredService<IWorkspaceIsolationService>();
            var yamlReader = scope.ServiceProvider.GetService<IWorktreeYamlReader>();

            RepositoryCloneResult clone;
            try
            {
                clone = await provisioning.EnsureCloneAsync(repo, cancellationToken).ConfigureAwait(false);
            }
            catch (DomainException ex)
            {
                return Error(ex.Message, "clone-failed");
            }

            // SPEC-20261021-worktree-setup-scripts RF-007: an explicit prompt
            // always wins; otherwise the repo's `issueCommand:` template (or
            // its config mirror) builds the instructions; fallback = the
            // classic "issue title + body" prompt.
            var prompt = ReadString(arguments, "prompt");
            if (string.IsNullOrWhiteSpace(prompt))
            {
                var template = yamlReader?.EffectiveIssueCommand(
                    yamlReader.Read(clone.Path));
                prompt = template is null
                    ? null
                    : IssueCommandTemplate.Render(
                        template, repo, issue.Number, issue.Title,
                        issue.Body, issue.HtmlUrl);
                prompt ??= $"GitHub issue {repo}#{issue.Number}: {issue.Title}\n\n"
                    + (string.IsNullOrWhiteSpace(issue.Body) ? "(no body)" : issue.Body);
            }

            var autonomy = ReadString(arguments, "autonomy");
            if (autonomy is not null
                && !Taskboard.Agents.AgentAutonomyNames.IsValidName(autonomy.Trim().ToLowerInvariant()))
            {
                return Error("autonomy must be plan|edit|full", "invalid autonomy");
            }

            var task = await delegation.CreateTaskAsync(
                new CreateDelegationTaskRequest(
                    prompt, cli.Trim(),
                    Taskboard.Integrations.Chat.Tools.Delegation.DelegationToolSupport.ScopeOf(context),
                    clone.Path,
                    DependsOn: null, RetryOf: null, FanoutGroupId: null,
                    UseWorktree: true, clone.Path, BaseCommitSha: null,
                    Autonomy: autonomy,
                    Kind: "issue", IssueRef: $"#{issue.Number}"),
                cancellationToken).ConfigureAwait(false);

            var (_, attached, provisionError) = await DelegationToolSupport
                .ProvisionAndAttachAsync(
                    isolation, delegation, task, clone.Path, "main", "issue",
                    cancellationToken,
                    taskSlug: $"issue-{issue.Number}-{task.Id[^Math.Min(8, task.Id.Length)..]}")
                .ConfigureAwait(false);
            if (provisionError is not null)
            {
                return Error(provisionError, "worktree error");
            }

            task = attached;

            await github.UpdateIssueColumnAsync(
                repo, issue.Number,
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
