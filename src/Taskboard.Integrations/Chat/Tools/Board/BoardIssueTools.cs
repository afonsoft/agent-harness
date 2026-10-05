using System.Text.Json;
using Taskboard.Application.Contracts.Chat;
using Taskboard.GitHub;
using static Taskboard.Integrations.Chat.Tools.Board.BoardToolSupport;

namespace Taskboard.Integrations.Chat.Tools.Board;

/// <summary>
/// SPEC-20261004-chat-board-tools RF-001: compact list of board cards — a card
/// is a GitHub issue, a column is a label (<see cref="GitHubBoardColumn"/>).
/// </summary>
public sealed class BoardListIssuesTool(IGitHubService github) : IChatTool
{
    public string Name => "board_list_issues";
    public string Description =>
        "List cards on the board for a GitHub repository. A card is a GitHub issue and its "
        + "column is a board label (backlog|todo|in-progress|in-review|in-pullrequest|blocked|done|canceled|archived). "
        + "Use this to see what is on the board before creating, moving or delegating cards.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "repository":{"type":"string","description":"GitHub repository 'owner/name'"},
          "column":{"type":"string","description":"Optional column filter"},
          "take":{"type":"integer","description":"Max cards (default 50)"}
        },"required":["repository"]}
        """;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var repo = RepoOf(arguments, out var error);
        if (error is not null)
        {
            return Task.FromResult(error);
        }

        return GuardAsync(async () =>
        {
            var columnFilter = ParseColumn(ReadString(arguments, "column"));
            var take = arguments.TryGetProperty("take", out var t) && t.TryGetInt32(out var n) && n > 0
                ? Math.Min(n, 100)
                : 50;

            var issues = await github.GetIssuesAsync(repo!, cancellationToken).ConfigureAwait(false);
            var cards = issues
                .Where(i => columnFilter is null || i.Column == columnFilter)
                .OrderBy(i => i.Column)
                .ThenBy(i => i.Number)
                .Take(take)
                .Select(i => new
                {
                    number = i.Number,
                    title = i.Title.Length > 120 ? i.Title[..120] : i.Title,
                    column = i.Column.ToDisplayName(),
                    labels = i.Labels,
                    url = i.HtmlUrl,
                    updatedAt = i.UpdatedAt
                });
            return Ok(new { repository = repo, issues = cards });
        });
    }
}

/// <summary>
/// SPEC-20261004-chat-board-tools RF-002: full issue body + last 5 comments.
/// </summary>
public sealed class BoardGetIssueTool(IGitHubService github) : IChatTool
{
    public string Name => "board_get_issue";
    public string Description =>
        "Get a board card (GitHub issue) by number: title, body, column, labels and the last "
        + "5 comments. Use before editing or commenting.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "repository":{"type":"string","description":"GitHub repository 'owner/name'"},
          "number":{"type":"integer","description":"Issue number"}
        },"required":["repository","number"]}
        """;

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

            var comments = await github.GetIssueCommentsAsync(repo!, number, take: 5, cancellationToken)
                .ConfigureAwait(false);
            return Ok(new
            {
                issue = new
                {
                    number = issue.Number,
                    title = issue.Title,
                    body = issue.Body,
                    column = issue.Column.ToDisplayName(),
                    labels = issue.Labels,
                    state = issue.State,
                    priority = issue.Priority,
                    url = issue.HtmlUrl
                },
                comments = comments.Select(c => new { c.AuthorLogin, c.Body, c.CreatedAt })
            });
        });
    }
}

/// <summary>
/// SPEC-20261004-chat-board-tools RF-003: create a card — one call lands on the
/// board AND on GitHub because they are the same object.
/// </summary>
public sealed class BoardCreateIssueTool(IGitHubService github) : IChatTool
{
    public string Name => "board_create_issue";
    public string Description =>
        "Create a card on the board. A card IS a GitHub issue — 'create on the board' and "
        + "'create on GitHub' are the same call, never do both. The optional column lands "
        + "the card directly in that column (default todo).";
    public string ParametersJson => """
        {"type":"object","properties":{
          "repository":{"type":"string","description":"GitHub repository 'owner/name'"},
          "title":{"type":"string","description":"Issue title"},
          "body":{"type":"string","description":"Issue body (markdown)"},
          "column":{"type":"string","description":"Initial column (default todo)"},
          "labels":{"type":"array","items":{"type":"string"},"description":"Extra labels (non-column)"}
        },"required":["repository","title"]}
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

        var title = ReadString(arguments, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return Task.FromResult(Error("title is required", "missing-title"));
        }

        GitHubBoardColumn? column = GitHubBoardColumn.Todo;
        if (ReadString(arguments, "column") is { Length: > 0 } raw)
        {
            column = ParseColumn(raw);
            if (column is null || !column.Value.HasLabel())
            {
                return Task.FromResult(Error(
                    $"column '{raw}' is invalid or derived — allowed: backlog|todo|in-progress|in-review|in-pullrequest|blocked|done|canceled",
                    "invalid-column"));
            }
        }

        return GuardAsync(async () =>
        {
            var issue = await github.CreateIssueAsync(
                repo!, title.Trim(), ReadString(arguments, "body"), column.Value, cancellationToken)
                .ConfigureAwait(false);

            var extra = ReadStringList(arguments, "labels")
                .Where(l => GitHubBoardColumnExtensions.FromLabel(l) is null)
                .ToArray();
            if (extra.Length > 0)
            {
                await github.AddLabelsToIssueAsync(repo!, issue.Number, extra, cancellationToken)
                    .ConfigureAwait(false);
            }

            return Ok(CardOf(issue));
        });
    }
}

/// <summary>
/// SPEC-20261004-chat-board-tools RF-005: edit title/body of a card.
/// </summary>
public sealed class BoardUpdateIssueTool(IGitHubService github) : IChatTool
{
    public string Name => "board_update_issue";
    public string Description =>
        "Update a board card's title and/or body (GitHub issue fields). At least one of "
        + "title/body is required.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "repository":{"type":"string","description":"GitHub repository 'owner/name'"},
          "number":{"type":"integer","description":"Issue number"},
          "title":{"type":"string","description":"New title"},
          "body":{"type":"string","description":"New body (markdown)"}
        },"required":["repository","number"]}
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

        var title = ReadString(arguments, "title");
        var body = ReadString(arguments, "body");
        if (title is null && body is null)
        {
            return Task.FromResult(Error("provide title and/or body", "nothing-to-update"));
        }

        return GuardAsync(async () =>
        {
            var issue = await github.UpdateIssueAsync(repo!, number, title, body, cancellationToken)
                .ConfigureAwait(false);
            return Ok(CardOf(issue));
        });
    }
}

/// <summary>
/// SPEC-20261004-chat-board-tools RF-006: add/remove non-column labels. Column
/// moves go through board_move_card so a card never carries two column labels.
/// </summary>
public sealed class BoardSetLabelsTool(IGitHubService github) : IChatTool
{
    public string Name => "board_set_labels";
    public string Description =>
        "Add and/or remove labels on a board card (GitHub issue). Column labels are rejected "
        + "here — use board_move_card to move a card between columns.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "repository":{"type":"string","description":"GitHub repository 'owner/name'"},
          "number":{"type":"integer","description":"Issue number"},
          "add":{"type":"array","items":{"type":"string"},"description":"Labels to add"},
          "remove":{"type":"array","items":{"type":"string"},"description":"Labels to remove"}
        },"required":["repository","number"]}
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

        var add = ReadStringList(arguments, "add");
        var remove = ReadStringList(arguments, "remove");
        if (add.Count == 0 && remove.Count == 0)
        {
            return Task.FromResult(Error("provide add and/or remove", "nothing-to-change"));
        }

        var columnLabel = add.FirstOrDefault(l => GitHubBoardColumnExtensions.FromLabel(l) is not null);
        if (columnLabel is not null)
        {
            return Task.FromResult(Error(
                $"'{columnLabel}' is a column label — use board_move_card to move the card",
                "column-label"));
        }

        return GuardAsync(async () =>
        {
            if (add.Count > 0)
            {
                await github.AddLabelsToIssueAsync(repo!, number, add, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (remove.Count > 0)
            {
                await github.RemoveLabelsFromIssueAsync(repo!, number, remove, cancellationToken)
                    .ConfigureAwait(false);
            }

            var issue = await github.GetIssueAsync(repo!, number, cancellationToken).ConfigureAwait(false);
            return Ok(issue is null ? new { number } : CardOf(issue));
        });
    }
}

/// <summary>
/// SPEC-20261004-chat-board-tools RF-008: swap the priority:* label.
/// </summary>
public sealed class BoardSetPriorityTool(IGitHubService github) : IChatTool
{
    public string Name => "board_set_priority";
    public string Description =>
        "Set the priority of a board card — swaps the priority:* label "
        + "(Urgent|High|Medium|Low; None clears it).";
    public string ParametersJson => """
        {"type":"object","properties":{
          "repository":{"type":"string","description":"GitHub repository 'owner/name'"},
          "number":{"type":"integer","description":"Issue number"},
          "priority":{"type":"string","description":"Urgent|High|Medium|Low|None"}
        },"required":["repository","number","priority"]}
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

        var priority = GitHubBoardColumnExtensions.NormalizePriority(ReadString(arguments, "priority"));
        if (priority is null)
        {
            return Task.FromResult(Error(
                "priority must be Urgent|High|Medium|Low|None", "invalid-priority"));
        }

        return GuardAsync(async () =>
        {
            var issue = await github.SetIssuePriorityAsync(repo!, number, priority, cancellationToken)
                .ConfigureAwait(false);
            return Ok(CardOf(issue));
        });
    }
}

/// <summary>
/// SPEC-20261004-chat-board-tools RF-007: close a card — done moves it to the
/// done column first; canceled applies the canceled label; archived closes as-is.
/// </summary>
public sealed class BoardCloseIssueTool(IGitHubService github) : IChatTool
{
    public string Name => "board_close_issue";
    public string Description =>
        "Close a board card (GitHub issue). resolution=done (default) moves the card to done "
        + "and closes; canceled labels it canceled; archived closes without a column label. "
        + "An optional reason is posted as a comment first.";
    public string ParametersJson => """
        {"type":"object","properties":{
          "repository":{"type":"string","description":"GitHub repository 'owner/name'"},
          "number":{"type":"integer","description":"Issue number"},
          "resolution":{"type":"string","description":"done|canceled|archived (default done)"},
          "reason":{"type":"string","description":"Optional closing note — posted as a comment"}
        },"required":["repository","number"]}
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

        var resolution = ReadString(arguments, "resolution") ?? "done";
        var valid = resolution.Equals("done", StringComparison.OrdinalIgnoreCase)
            || resolution.Equals("canceled", StringComparison.OrdinalIgnoreCase)
            || resolution.Equals("archived", StringComparison.OrdinalIgnoreCase);
        if (!valid)
        {
            return Task.FromResult(Error("resolution must be done|canceled|archived", "invalid-resolution"));
        }

        var reason = ReadString(arguments, "reason");
        var isDone = resolution.Equals("done", StringComparison.OrdinalIgnoreCase);

        return GuardAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                await github.AddIssueCommentAsync(repo!, number, reason, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (isDone)
            {
                var current = await github.GetIssueAsync(repo!, number, cancellationToken)
                    .ConfigureAwait(false);
                if (current is null)
                {
                    return Error($"issue #{number} not found", "not-found");
                }

                await github.UpdateIssueColumnAsync(
                    repo!, number,
                    current.Column.HasLabel() ? current.Column : null,
                    GitHubBoardColumn.Done, cancellationToken).ConfigureAwait(false);
            }

            var issue = await github.CloseIssueAsync(
                repo!, number, isDone ? "archived" : resolution, cancellationToken)
                .ConfigureAwait(false);
            return Ok(CardOf(issue));
        });
    }
}

/// <summary>
/// SPEC-20261004-chat-board-tools RF-007: post a comment on a card.
/// </summary>
public sealed class BoardCommentTool(IGitHubService github) : IChatTool
{
    public string Name => "board_comment";
    public string Description =>
        "Post a comment on a board card (GitHub issue).";
    public string ParametersJson => """
        {"type":"object","properties":{
          "repository":{"type":"string","description":"GitHub repository 'owner/name'"},
          "number":{"type":"integer","description":"Issue number"},
          "body":{"type":"string","description":"Comment body (markdown)"}
        },"required":["repository","number","body"]}
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

        var body = ReadString(arguments, "body");
        if (string.IsNullOrWhiteSpace(body))
        {
            return Task.FromResult(Error("body is required", "missing-body"));
        }

        return GuardAsync(async () =>
        {
            var comment = await github.AddIssueCommentAsync(repo!, number, body, cancellationToken)
                .ConfigureAwait(false);
            return Ok(new { commentId = comment?.Id, url = comment?.HtmlUrl });
        });
    }
}
