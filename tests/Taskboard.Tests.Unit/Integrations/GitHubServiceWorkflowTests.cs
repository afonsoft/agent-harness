using System.Net;
using NSubstitute;
using Octokit;
using Octokit.Internal;
using Shouldly;
using Taskboard.Integrations.GitHub;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations;

/// <summary>
/// SPEC-20260922-workflow-actions-resilience RF-001/RF-002/RF-003 — the
/// workflows monitor must fetch runs repo-wide in one call, group them by
/// workflow, expose the latest 5 and degrade (never fail) when the runs call
/// is slow or broken.
/// </summary>
public class GitHubServiceWorkflowTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Workflow Workflow(long id, string name) => new(
        id, $"node{id}", name, $".github/workflows/{name}.yml", WorkflowState.Active,
        Now.AddDays(-30), Now.AddDays(-1),
        $"https://api.github.com/x/y/actions/workflows/{id}",
        $"https://github.com/x/y/actions/workflows/{name}.yml",
        $"https://github.com/x/y/badge/{id}", null);

    private static WorkflowRun Run(long id, long workflowId, string name, DateTimeOffset createdAt) => new(
        id, name, $"node{id}", 1, "csn", "main", "0123456789abcdef",
        $".github/workflows/{name}.yml", id, "push", $"run {id}",
        WorkflowRunStatus.Completed, WorkflowRunConclusion.Success, workflowId,
        $"https://api.github.com/x/y/actions/runs/{id}",
        $"https://github.com/x/y/actions/runs/{id}",
        null, createdAt, createdAt.AddMinutes(2), null, 1, null, createdAt, null,
        "jobs", "logs", "check", "artifacts", "cancel", "rerun", "prev",
        $"https://api.github.com/x/y/actions/workflows/{workflowId}",
        null, null, null, 1);

    private static ApiInfo EmptyApiInfo => new(
        new Dictionary<string, Uri>(), new List<string>(), new List<string>(),
        null, null, TimeSpan.Zero);

    // Octokit pagination calls IConnection.Get<List<T>>(uri, params, accepts,
    // ct, preprocess) — the page is a List<T> wrapper around each response.
    private static void SetupPagedGet<T>(IConnection connection, Func<Task<IApiResponse<List<T>>>> page)
        where T : class
    {
        connection
            .Get<List<T>>(
                Arg.Any<Uri>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Func<object, object>>())
            .Returns(_ => page());
    }

    private static IApiResponse<List<T>> Page<T>(T body) where T : class
    {
        var httpResponse = Substitute.For<IResponse>();
        httpResponse.ApiInfo.Returns(EmptyApiInfo);
        return new ApiResponse<List<T>>(httpResponse, [body]);
    }

    private static IConnection Connection(
        IReadOnlyList<Workflow> workflows,
        Func<Task<IApiResponse<List<WorkflowRunsResponse>>>> runsResponse)
    {
        var connection = Substitute.For<IConnection>();
        connection.BaseAddress.Returns(new Uri("https://api.github.com/"));
        connection.Credentials.Returns(new Credentials("fake-token"));

        SetupPagedGet<WorkflowsResponse>(
            connection,
            () => Task.FromResult(
                Page(new WorkflowsResponse(workflows.Count, workflows))));
        SetupPagedGet<WorkflowRunsResponse>(connection, runsResponse);

        return connection;
    }

    private static Task<IApiResponse<List<WorkflowRunsResponse>>> OkRuns(
        IReadOnlyList<WorkflowRun> runs) =>
        Task.FromResult(Page(new WorkflowRunsResponse(runs.Count, runs)));

    [Fact]
    public async Task Dado_WorkflowsComRuns_Quando_GetWorkflows_Entao_LastRunPorWorkflow()
    {
        var connection = Connection(
            [Workflow(11, "ci"), Workflow(22, "release")],
            () => OkRuns(
            [
                Run(101, 11, "ci", Now.AddHours(-2)),
                Run(102, 11, "ci", Now.AddMinutes(-10)),
                Run(103, 22, "release", Now.AddHours(-5)),
            ]));

        var service = new GitHubService(connection);

        var monitor = await service.GetWorkflowsAsync("x/y");

        monitor.Degraded.ShouldBeFalse();
        monitor.Workflows.Count.ShouldBe(2);
        monitor.Workflows[0].Id.ShouldBe(11);
        monitor.Workflows[0].LastRun!.Id.ShouldBe(102);
        monitor.Workflows[1].Id.ShouldBe(22);
        monitor.Workflows[1].LastRun!.Id.ShouldBe(103);
    }

    [Fact]
    public async Task Dado_RepoComMuitosRuns_Quando_GetWorkflows_Entao_RecentRunsTop5()
    {
        var runs = Enumerable.Range(1, 7)
            .Select(i => Run(i, 11, "ci", Now.AddMinutes(-i * 10)))
            .ToList();
        var connection = Connection([Workflow(11, "ci")], () => OkRuns(runs));

        var service = new GitHubService(connection);

        var monitor = await service.GetWorkflowsAsync("x/y");

        monitor.RecentRuns.Count.ShouldBe(5);
        monitor.RecentRuns.Select(r => r.Id).ShouldBe([1, 2, 3, 4, 5]);
        monitor.RecentRuns.ShouldAllBe(r => r.WorkflowId == 11);
    }

    [Fact]
    public async Task Dado_RunsCallFalha_Quando_GetWorkflows_Entao_DegradaParcial()
    {
        var connection = Connection(
            [Workflow(11, "ci")],
            () => throw new ApiException("boom", HttpStatusCode.Forbidden));

        var service = new GitHubService(connection);

        var monitor = await service.GetWorkflowsAsync("x/y");

        monitor.Degraded.ShouldBeTrue();
        monitor.RecentRuns.ShouldBeEmpty();
        monitor.Workflows.Count.ShouldBe(1);
        monitor.Workflows[0].LastRun.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RunsCallLenta_Quando_DeadlineEstoura_Entao_DegradaParcial()
    {
        var never = new TaskCompletionSource<IApiResponse<List<WorkflowRunsResponse>>>();
        var connection = Connection([Workflow(11, "ci")], () => never.Task);

        var service = new GitHubService(connection);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var monitor = await service.GetWorkflowsAsync("x/y", cts.Token);

        monitor.Degraded.ShouldBeTrue();
        monitor.Workflows.Count.ShouldBe(1);
        monitor.Workflows[0].LastRun.ShouldBeNull();
        monitor.RecentRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_RepositorioInexistente_Quando_GetWorkflows_Entao_404Propaga()
    {
        var connection = Substitute.For<IConnection>();
        connection.BaseAddress.Returns(new Uri("https://api.github.com/"));
        connection.Credentials.Returns(new Credentials("fake-token"));
        SetupPagedGet<WorkflowsResponse>(
            connection,
            () => throw new ApiException("Not Found", HttpStatusCode.NotFound));

        var service = new GitHubService(connection);

        var ex = await Should.ThrowAsync<ApiException>(() => service.GetWorkflowsAsync("x/y"));
        ex.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
