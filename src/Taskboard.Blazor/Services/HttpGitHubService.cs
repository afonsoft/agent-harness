using System.Net;
using System.Net.Http.Json;
using Taskboard.GitHub;

namespace Taskboard.Blazor.Services;

/// <summary>
/// <see cref="IGitHubService"/> implementation backed by the server-side
/// <c>/api/github/*</c> endpoints (SPEC-20260915-blazor-wasm-migration). The
/// GitHub token never leaves the server — the browser only sees DTOs.
/// </summary>
public sealed class HttpGitHubService(HttpClient http) : IGitHubService
{
    /// <summary>Token configuration is server-side; a no-op on the client.</summary>
    public void SetToken(string token)
    {
    }

    public async Task<IReadOnlyList<RepositoryDto>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync("/api/github/repositories", cancellationToken);
        await ThrowOnTokenMissingAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RepositoriesResponse>(cancellationToken);
        return result?.Repositories ?? [];
    }

    public async Task<IReadOnlyList<IssueDto>> GetIssuesAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var response = await http.GetAsync($"/api/github/repos/{owner}/{repo}/issues", cancellationToken);
        await ThrowOnTokenMissingAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IssuesResponse>(cancellationToken);
        return result?.Issues ?? [];
    }

    public async Task<IssueDto> UpdateIssueColumnAsync(
        string repositoryFullName,
        int issueNumber,
        GitHubBoardColumn? oldColumn,
        GitHubBoardColumn newColumn,
        CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var response = await http.PutAsJsonAsync(
            $"/api/github/repos/{owner}/{repo}/issues/{issueNumber}/column",
            new UpdateColumnRequest(oldColumn, newColumn),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IssueResponse>(cancellationToken);
        return result!.Issue;
    }

    public async Task<IssueDto> CreateIssueAsync(
        string repositoryFullName,
        string title,
        string? body,
        GitHubBoardColumn initialColumn,
        CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var response = await http.PostAsJsonAsync(
            $"/api/github/repos/{owner}/{repo}/issues",
            new CreateIssueRequest(title, body, initialColumn),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IssueResponse>(cancellationToken);
        return result!.Issue;
    }

    public async Task AddLabelsToIssueAsync(
        string repositoryFullName,
        int issueNumber,
        IReadOnlyCollection<string> labels,
        CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var response = await http.PostAsJsonAsync(
            $"/api/github/repos/{owner}/{repo}/issues/{issueNumber}/labels",
            new AddLabelsRequest(labels),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IssueDto> UpdateIssueAsync(
        string repositoryFullName,
        int issueNumber,
        string? title,
        string? body,
        CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var request = new HttpRequestMessage(HttpMethod.Patch,
            $"/api/github/repos/{owner}/{repo}/issues/{issueNumber}")
        {
            Content = JsonContent.Create(new UpdateIssueRequest(title, body)),
        };
        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IssueResponse>(cancellationToken);
        return result!.Issue;
    }

    public async Task<IssueDto> SetIssuePriorityAsync(
        string repositoryFullName,
        int issueNumber,
        string priority,
        CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var response = await http.PutAsJsonAsync(
            $"/api/github/repos/{owner}/{repo}/issues/{issueNumber}/priority",
            new SetPriorityRequest(priority),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IssueResponse>(cancellationToken);
        return result!.Issue;
    }

    public async Task<IssueDto> CloseIssueAsync(
        string repositoryFullName,
        int issueNumber,
        string resolution,
        CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var response = await http.PostAsJsonAsync(
            $"/api/github/repos/{owner}/{repo}/issues/{issueNumber}/close",
            new CloseIssueRequest(resolution),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IssueResponse>(cancellationToken);
        return result!.Issue;
    }

    public async Task<IReadOnlyList<IssueCommentDto>> GetIssueCommentsAsync(
        string repositoryFullName,
        int issueNumber,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var response = await http.GetAsync(
            $"/api/github/repos/{owner}/{repo}/issues/{issueNumber}/comments?take={take}",
            cancellationToken);
        await ThrowOnTokenMissingAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CommentsResponse>(cancellationToken);
        return result?.Comments ?? [];
    }

    public async Task<IssueCommentDto> AddIssueCommentAsync(
        string repositoryFullName,
        int issueNumber,
        string body,
        CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var response = await http.PostAsJsonAsync(
            $"/api/github/repos/{owner}/{repo}/issues/{issueNumber}/comments",
            new AddCommentRequest(body),
            cancellationToken);
        await ThrowOnTokenMissingAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CommentResponse>(cancellationToken);
        return result!.Comment;
    }

    public async Task<IReadOnlyList<MilestoneDto>> GetMilestonesAsync(
        string repositoryFullName,
        CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var result = await http.GetFromJsonAsync<RepoTimelineDto>(
            $"/api/github/repos/{owner}/{repo}/timeline?days=90",
            cancellationToken);
        return result?.Milestones ?? [];
    }

    public Task<IReadOnlyList<IssueLabelEventDto>> GetIssueTimelineEventsAsync(
        string repositoryFullName,
        int issueNumber,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Raw label events are server-side only — the client reads the aggregated timeline via ITimelineMetricsService.");

    public async Task<IReadOnlyList<WorkflowDto>> GetWorkflowsAsync(
        string repositoryFullName,
        CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var response = await http.GetAsync($"/api/github/repos/{owner}/{repo}/workflows", cancellationToken);
        await ThrowOnTokenMissingAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WorkflowsResponse>(cancellationToken);
        return result?.Workflows ?? [];
    }

    public async Task<IReadOnlyList<WorkflowRunDto>> GetWorkflowRunsAsync(
        string repositoryFullName,
        long workflowId,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        var (owner, repo) = SplitFullName(repositoryFullName);
        var response = await http.GetAsync(
            $"/api/github/repos/{owner}/{repo}/workflows/{workflowId}/runs?take={take}",
            cancellationToken);
        await ThrowOnTokenMissingAsync(response, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WorkflowRunsResponse>(cancellationToken);
        return result?.Runs ?? [];
    }

    private static (string Owner, string Repo) SplitFullName(string fullName)
    {
        var parts = fullName.Split('/', 2, StringSplitOptions.TrimEntries);
        return (Uri.EscapeDataString(parts[0]), Uri.EscapeDataString(parts[1]));
    }

    private static async Task ThrowOnTokenMissingAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            return;
        }

        var error = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(cancellationToken);
        if (error?.Error?.Code == "GITHUB_TOKEN_MISSING")
        {
            throw new InvalidOperationException(error.Error.Message);
        }
    }

    private sealed record RepositoriesResponse(List<RepositoryDto> Repositories);
    private sealed record IssuesResponse(List<IssueDto> Issues);
    private sealed record IssueResponse(IssueDto Issue);
    private sealed record UpdateColumnRequest(GitHubBoardColumn? OldColumn, GitHubBoardColumn NewColumn);
    private sealed record CreateIssueRequest(string Title, string? Body, GitHubBoardColumn InitialColumn);
    private sealed record AddLabelsRequest(IReadOnlyCollection<string> Labels);
    private sealed record UpdateIssueRequest(string? Title, string? Body);
    private sealed record SetPriorityRequest(string Priority);
    private sealed record CloseIssueRequest(string Resolution);
    private sealed record CommentsResponse(List<IssueCommentDto> Comments);
    private sealed record CommentResponse(IssueCommentDto Comment);
    private sealed record WorkflowsResponse(List<WorkflowDto> Workflows);
    private sealed record WorkflowRunsResponse(List<WorkflowRunDto> Runs);
    private sealed record AddCommentRequest(string Body);
    private sealed record ErrorEnvelope(ErrorBody? Error);
    private sealed record ErrorBody(string Code, string Message);
}
