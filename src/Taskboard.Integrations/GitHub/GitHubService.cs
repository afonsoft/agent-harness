using Octokit;
using Taskboard.GitHub;

namespace Taskboard.Integrations.GitHub;

/// <summary>
/// Implementação do <see cref="IGitHubService"/> usando a biblioteca oficial Octokit.
/// </summary>
public sealed class GitHubService : IGitHubService
{
    private const string ProductName = "TaskBoardAI";
    private readonly GitHubClient _client;

    /// <summary>
    /// Cria uma nova instância do serviço de integração com GitHub.
    /// </summary>
    public GitHubService()
    {
        _client = new GitHubClient(new ProductHeaderValue(ProductName));

        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            _client.Credentials = new Credentials(token);
        }
    }

    /// <inheritdoc />
    public void SetToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _client.Credentials = new Credentials(token);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepositoryDto>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var apiOptions = new ApiOptions { PageSize = 100 };
        var repositories = await _client.Repository.GetAllForCurrent(apiOptions);

        return repositories
            .OrderBy(r => r.FullName)
            .Select(MapToDto)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IssueDto>> GetIssuesAsync(
        string repositoryFullName,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var (owner, name) = SplitRepositoryName(repositoryFullName);

        var request = new RepositoryIssueRequest
        {
            State = ItemStateFilter.All,
            SortProperty = IssueSort.Created,
            SortDirection = SortDirection.Descending
        };

        var issues = await _client.Issue.GetAllForRepository(owner, name, request);
        var now = DateTimeOffset.UtcNow;
        return issues
            .Select(i => MapToDto(i, repositoryFullName))
            .Where(dto => GitHubBoardGrouper.IsVisible(dto, now))
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IssueDto> UpdateIssueColumnAsync(
        string repositoryFullName,
        int issueNumber,
        GitHubBoardColumn? oldColumn,
        GitHubBoardColumn newColumn,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        if (!newColumn.HasLabel())
        {
            throw new ArgumentOutOfRangeException(nameof(newColumn), newColumn, "Column is derived and cannot be assigned via label.");
        }

        var (owner, name) = SplitRepositoryName(repositoryFullName);
        var newLabel = newColumn.ToLabel();

        if (oldColumn.HasValue && oldColumn.Value != newColumn && oldColumn.Value.HasLabel())
        {
            var oldLabel = oldColumn.Value.ToLabel();
            try
            {
                await _client.Issue.Labels.RemoveFromIssue(owner, name, issueNumber, oldLabel);
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Label já removida; prossegue.
            }
        }

        await EnsureLabelExistsAsync(owner, name, newLabel, cancellationToken);
        await _client.Issue.Labels.AddToIssue(owner, name, issueNumber, [newLabel]);

        var updatedIssue = await _client.Issue.Get(owner, name, issueNumber);
        return MapToDto(updatedIssue, repositoryFullName);
    }

    /// <inheritdoc />
    public async Task<IssueDto> CreateIssueAsync(
        string repositoryFullName,
        string title,
        string? body,
        GitHubBoardColumn initialColumn,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var (owner, name) = SplitRepositoryName(repositoryFullName);

        await EnsureLabelExistsAsync(owner, name, initialColumn.ToLabel(), cancellationToken);

        var newIssue = new NewIssue(title)
        {
            Body = body
        };
        newIssue.Labels.Add(initialColumn.ToLabel());

        var issue = await _client.Issue.Create(owner, name, newIssue);
        return MapToDto(issue, repositoryFullName);
    }

    /// <inheritdoc />
    public async Task AddLabelsToIssueAsync(
        string repositoryFullName,
        int issueNumber,
        IReadOnlyCollection<string> labels,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var (owner, name) = SplitRepositoryName(repositoryFullName);

        if (labels.Count == 0)
        {
            return;
        }

        foreach (var label in labels)
        {
            await EnsureLabelExistsAsync(owner, name, label, cancellationToken);
        }

        await _client.Issue.Labels.AddToIssue(owner, name, issueNumber, labels.ToArray());
    }

    /// <inheritdoc />
    public async Task<IssueDto> UpdateIssueAsync(
        string repositoryFullName,
        int issueNumber,
        string? title,
        string? body,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var (owner, name) = SplitRepositoryName(repositoryFullName);

        var update = new IssueUpdate { Body = body };
        if (!string.IsNullOrWhiteSpace(title))
        {
            update.Title = title;
        }

        var issue = await _client.Issue.Update(owner, name, issueNumber, update);
        return MapToDto(issue, repositoryFullName);
    }

    /// <inheritdoc />
    public async Task<IssueDto> SetIssuePriorityAsync(
        string repositoryFullName,
        int issueNumber,
        string priority,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var (owner, name) = SplitRepositoryName(repositoryFullName);
        var normalized = GitHubBoardColumnExtensions.NormalizePriority(priority)
            ?? throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority.");

        var issue = await _client.Issue.Get(owner, name, issueNumber);
        foreach (var label in issue.Labels.Select(l => l.Name).Where(GitHubBoardColumnExtensions.IsPriorityLabel))
        {
            try
            {
                await _client.Issue.Labels.RemoveFromIssue(owner, name, issueNumber, label);
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Label já removida; prossegue.
            }
        }

        var newLabel = GitHubBoardColumnExtensions.ToPriorityLabel(normalized);
        if (newLabel is not null)
        {
            await EnsureLabelExistsAsync(owner, name, newLabel, cancellationToken);
            await _client.Issue.Labels.AddToIssue(owner, name, issueNumber, [newLabel]);
        }

        var updated = await _client.Issue.Get(owner, name, issueNumber);
        return MapToDto(updated, repositoryFullName);
    }

    /// <inheritdoc />
    public async Task<IssueDto> CloseIssueAsync(
        string repositoryFullName,
        int issueNumber,
        string resolution,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var (owner, name) = SplitRepositoryName(repositoryFullName);

        if (string.Equals(resolution, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            var label = GitHubBoardColumn.Canceled.ToLabel();
            await EnsureLabelExistsAsync(owner, name, label, cancellationToken);
            await _client.Issue.Labels.AddToIssue(owner, name, issueNumber, [label]);
        }
        else if (!string.Equals(resolution, "archived", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Resolution must be 'canceled' or 'archived'.");
        }

        var closed = await _client.Issue.Update(owner, name, issueNumber,
            new IssueUpdate { State = ItemState.Closed });
        return MapToDto(closed, repositoryFullName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IssueCommentDto>> GetIssueCommentsAsync(
        string repositoryFullName,
        int issueNumber,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var (owner, name) = SplitRepositoryName(repositoryFullName);

        var comments = await _client.Issue.Comment.GetAllForIssue(owner, name, issueNumber);
        return comments
            .OrderBy(c => c.CreatedAt)
            .TakeLast(take)
            .Select(MapToDto)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IssueCommentDto> AddIssueCommentAsync(
        string repositoryFullName,
        int issueNumber,
        string body,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        var (owner, name) = SplitRepositoryName(repositoryFullName);

        var comment = await _client.Issue.Comment.Create(owner, name, issueNumber, body);
        return MapToDto(comment);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MilestoneDto>> GetMilestonesAsync(
        string repositoryFullName,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var (owner, name) = SplitRepositoryName(repositoryFullName);

        var request = new MilestoneRequest { State = ItemStateFilter.All };
        var milestones = await _client.Issue.Milestone.GetAllForRepository(owner, name, request);
        return milestones
            .OrderBy(m => m.Number)
            .Select(m => new MilestoneDto(
                m.Number,
                m.Title,
                m.DueOn,
                m.State.StringValue))
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IssueLabelEventDto>> GetIssueTimelineEventsAsync(
        string repositoryFullName,
        int issueNumber,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var (owner, name) = SplitRepositoryName(repositoryFullName);

        var events = await _client.Issue.Events.GetAllForIssue(owner, name, issueNumber);
        return events
            .Where(e => e.Label is not null
                && (e.Event == EventInfoState.Labeled || e.Event == EventInfoState.Unlabeled))
            .Select(e => new IssueLabelEventDto(
                e.CreatedAt,
                e.Label.Name,
                e.Event == EventInfoState.Labeled))
            .OrderBy(e => e.At)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowDto>> GetWorkflowsAsync(
        string repositoryFullName,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var (owner, name) = SplitRepositoryName(repositoryFullName);

        var response = await _client.Actions.Workflows.List(owner, name);
        var workflows = response.Workflows;

        // Last-run fetch per workflow: capped at 20, at most 8 concurrent —
        // repos with many workflows stay responsive (SPEC guardrail).
        const int maxLastRunFetches = 20;
        using var semaphore = new SemaphoreSlim(8);
        var enriched = await Task.WhenAll(workflows.Take(maxLastRunFetches).Select(async w =>
        {
            WorkflowRunDto? lastRun = null;
            try
            {
                await semaphore.WaitAsync(cancellationToken);
                var runs = await _client.Actions.Workflows.Runs.ListByWorkflow(
                    owner, name, w.Id, new WorkflowRunsRequest(), new ApiOptions { PageSize = 1 });
                lastRun = runs.WorkflowRuns.FirstOrDefault() is { } r ? MapRun(r) : null;
            }
            catch (ApiException)
            {
                // Best-effort: a workflow whose runs are not listable keeps no badge.
            }
            finally
            {
                semaphore.Release();
            }

            return MapWorkflow(w, lastRun);
        }));

        var rest = workflows.Skip(maxLastRunFetches).Select(w => MapWorkflow(w, null));
        return enriched.Concat(rest)
            .OrderByDescending(w => w.LastRun?.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowRunDto>> GetWorkflowRunsAsync(
        string repositoryFullName,
        long workflowId,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var (owner, name) = SplitRepositoryName(repositoryFullName);

        var runs = await _client.Actions.Workflows.Runs.ListByWorkflow(
            owner, name, workflowId, new WorkflowRunsRequest(), new ApiOptions { PageSize = take });
        return runs.WorkflowRuns
            .Take(take)
            .Select(MapRun)
            .ToList()
            .AsReadOnly();
    }

    private static WorkflowDto MapWorkflow(Workflow workflow, WorkflowRunDto? lastRun) => new(
        workflow.Id,
        workflow.Name,
        workflow.Path,
        workflow.State.StringValue,
        workflow.HtmlUrl,
        lastRun);

    private static WorkflowRunDto MapRun(WorkflowRun run) => new(
        run.Id,
        run.Name,
        run.DisplayTitle,
        run.RunNumber,
        run.Event,
        run.Status.StringValue,
        run.Conclusion?.StringValue,
        run.HeadBranch,
        run.HeadSha,
        run.Actor?.Login,
        run.CreatedAt,
        run.UpdatedAt,
        run.RunStartedAt,
        run.HtmlUrl);

    private static IssueCommentDto MapToDto(IssueComment comment) => new(
        comment.Id,
        comment.User?.Login,
        comment.Body,
        comment.CreatedAt,
        comment.UpdatedAt,
        comment.HtmlUrl);

    private async Task EnsureLabelExistsAsync(string owner, string name, string label, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Issue.Labels.Get(owner, name, label);
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await _client.Issue.Labels.Create(owner, name, new NewLabel(label, "ededed"));
        }
    }

    private static RepositoryDto MapToDto(Repository repository) => new(
        repository.Id,
        repository.FullName,
        repository.Name,
        repository.Description,
        repository.HtmlUrl,
        repository.Private);

    private static IssueDto MapToDto(Issue issue, string repositoryFullName)
    {
        var labels = issue.Labels?.Select(l => l.Name).ToList() ?? [];

        var dto = new IssueDto(
            issue.Id,
            issue.Number,
            issue.Title,
            issue.Body,
            issue.State.StringValue,
            issue.Url,
            issue.HtmlUrl,
            labels,
            GitHubBoardColumn.Backlog,
            issue.Assignee?.Login,
            GitHubBoardColumnExtensions.ResolvePriority(labels),
            issue.CreatedAt,
            issue.UpdatedAt,
            issue.ClosedAt,
            issue.Milestone?.Number,
            issue.Milestone?.DueOn);

        return dto with { Column = GitHubBoardGrouper.ResolveColumn(dto) };
    }

    private static (string Owner, string Name) SplitRepositoryName(string repositoryFullName)
    {
        var parts = repositoryFullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Repository name must be in the format 'owner/name'. Value: '{repositoryFullName}'", nameof(repositoryFullName));
        }

        return (parts[0], parts[1]);
    }

    private void EnsureAuthenticated()
    {
        if (_client.Credentials is null || string.IsNullOrWhiteSpace(_client.Credentials.Password))
        {
            throw new InvalidOperationException("GitHub token not configured. Set the GITHUB_TOKEN environment variable or call SetToken.");
        }
    }
}
