using System.Text.Json;
using Taskboard.Application.Contracts.Chat;
using Taskboard.GitHub;
using static Taskboard.Integrations.Chat.Tools.Board.BoardToolSupport;

namespace Taskboard.Integrations.Chat.Tools.Board;

/// <summary>
/// SPEC-20261004-chat-board-tools RF-004: move a card between columns — resolves
/// the current column from the issue's labels, then swaps the label.
/// </summary>
public sealed class BoardMoveCardTool(IGitHubService github) : IChatTool
{
    public string Name => "board_move_card";
    public string Description =>
        "Move a board card to another column. Columns are GitHub labels "
        + "(backlog|todo|in-progress|in-review|in-pullrequest|blocked|done|canceled); the previous "
        + "column label is removed automatically. 'archived' is derived and cannot be assigned.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "repository":{"type":"string","description":"GitHub repository 'owner/name'"},
          "number":{"type":"integer","description":"Issue number"},
          "column":{"type":"string","description":"Target column"}
        },"required":["repository","number","column"]}
        """;

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

        var column = RequireSettableColumn(arguments, out var columnError);
        if (columnError is not null)
        {
            return Task.FromResult(columnError);
        }

        return GuardAsync(async () =>
        {
            var issue = await github.GetIssueAsync(repo!, number, cancellationToken).ConfigureAwait(false);
            if (issue is null)
            {
                return Error($"issue #{number} not found", "not-found");
            }

            var updated = await github.UpdateIssueColumnAsync(
                repo!, number,
                issue.Column.HasLabel() ? issue.Column : null,
                column!.Value, cancellationToken).ConfigureAwait(false);
            return Ok(CardOf(updated));
        });
    }
}
