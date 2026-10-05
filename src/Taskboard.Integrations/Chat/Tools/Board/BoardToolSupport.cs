using System.Text.Json;
using Taskboard.Application.Contracts.Chat;
using Taskboard.GitHub;

namespace Taskboard.Integrations.Chat.Tools.Board;

/// <summary>
/// Shared helpers of the SPEC-20261004-chat-board-tools tools: repository
/// validation, column parsing and common readers/errors.
/// </summary>
internal static class BoardToolSupport
{
    /// <summary>
    /// Validates the <c>repository</c> argument in <c>owner/name</c> shape.
    /// </summary>
    public static string? RepoOf(JsonElement args, out ChatToolResult? error)
    {
        var raw = ReadString(args, "repository")?.Trim();
        var parts = raw?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 2 })
        {
            error = Error("repository must be 'owner/name'", "invalid-repository");
            return null;
        }

        error = null;
        return raw;
    }

    /// <summary>
    /// Parses a board column name into <see cref="GitHubBoardColumn"/>, accepting
    /// the label form (<c>in-review</c>), the display form (<c>in_review</c>) and
    /// the spaced form (<c>in review</c>).
    /// </summary>
    public static GitHubBoardColumn? ParseColumn(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return GitHubBoardColumnExtensions.FromLabel(trimmed)
            ?? GitHubBoardColumnExtensions.FromLabel(trimmed.Replace('_', '-').Replace(' ', '-'));
    }

    /// <summary>
    /// Parses the required <c>column</c> argument and rejects derived columns —
    /// only label-backed columns can be assigned.
    /// </summary>
    public static GitHubBoardColumn? RequireSettableColumn(JsonElement args, out ChatToolResult? error)
    {
        var raw = ReadString(args, "column");
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = Error("column is required", "missing-column");
            return null;
        }

        var column = ParseColumn(raw);
        if (column is null)
        {
            error = Error($"unknown column '{raw}' — allowed: backlog|todo|in-progress|in-review|in-pullrequest|blocked|done|canceled", "invalid-column");
            return null;
        }

        if (!column.Value.HasLabel())
        {
            error = Error(
                $"column '{raw}' is derived and cannot be assigned — allowed: backlog|todo|in-progress|in-review|in-pullrequest|blocked|done|canceled",
                "derived-column");
            return null;
        }

        error = null;
        return column;
    }

    public static int? NumberOf(JsonElement args)
    {
        return args.TryGetProperty("number", out var el) && el.TryGetInt32(out var n) && n > 0
            ? n
            : null;
    }

    public static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    public static IReadOnlyList<string> ReadStringList(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
            {
                list.Add(s.Trim());
            }
        }

        return list;
    }

    public static ChatToolResult Error(string message, string reason) =>
        new(JsonSerializer.Serialize(new { error = message }), Refused: true, reason);

    public static ChatToolResult Ok(object payload) =>
        new(JsonSerializer.Serialize(payload));

    /// <summary>Maps an issue to the compact card shape used by every tool.</summary>
    public static object CardOf(IssueDto issue) => new
    {
        number = issue.Number,
        title = issue.Title,
        column = issue.Column.ToDisplayName(),
        labels = issue.Labels,
        state = issue.State,
        priority = issue.Priority,
        url = issue.HtmlUrl,
        updatedAt = issue.UpdatedAt
    };

    /// <summary>
    /// Runs a GitHub-backed operation mapping the two predictable failure modes —
    /// missing token (<see cref="InvalidOperationException"/>) and Octokit 404 —
    /// to refused results instead of throwing.
    /// </summary>
    public static async Task<ChatToolResult> GuardAsync(
        Func<Task<ChatToolResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return Error(ex.Message, "github-auth");
        }
        catch (Octokit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Error("issue or repository not found", "not-found");
        }
    }
}
