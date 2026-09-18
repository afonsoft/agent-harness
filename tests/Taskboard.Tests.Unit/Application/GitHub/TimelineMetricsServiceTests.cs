using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Taskboard.Application.GitHub;
using Taskboard.Domain.Issues;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.GitHub;
using Taskboard.Issues;
using Xunit;

namespace Taskboard.Tests.Unit.Application.GitHub;

public class TimelineMetricsServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TaskboardDbContext _context;

    public TimelineMetricsServiceTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-gantt-{Guid.NewGuid()}.sqlite");
        var options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _context = new TaskboardDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private TimelineMetricsService CriarServico(IGitHubService gitHub) =>
        new(gitHub, new EfCoreRepository<IssueHistoryEvent>(_context),
            NullLogger<TimelineMetricsService>.Instance);

    private static IssueDto Issue(
        long id,
        int number,
        DateTimeOffset createdAt,
        DateTimeOffset? closedAt = null,
        GitHubBoardColumn column = GitHubBoardColumn.Todo,
        int? milestoneNumber = null,
        DateTimeOffset? milestoneDueOn = null) =>
        new(
            id, number, $"issue {number}", null,
            closedAt is null ? "open" : "closed",
            "https://api.github.com/x", "https://github.com/x",
            [], column, null, "None",
            createdAt, null, closedAt,
            milestoneNumber, milestoneDueOn);

    [Fact]
    public void Dado_LabelEventsOrdenados_Quando_BuildTransitions_Entao_ReconstroiSequencia()
    {
        // Covers RF-001: labeled events replay into column transitions
        var t1 = DateTimeOffset.UtcNow.AddDays(-10);
        var t2 = DateTimeOffset.UtcNow.AddDays(-5);
        var events = new List<IssueLabelEventDto>
        {
            new(t1, "todo", Added: true),
            new(t2, "in-progress", Added: true)
        };

        var transitions = TimelineMetricsService.BuildTransitions(events, null);

        transitions.Count.ShouldBe(2);
        transitions[0].ShouldBe(new IssueTransitionDto(t1, null, "todo"));
        transitions[1].ShouldBe(new IssueTransitionDto(t2, "todo", "in-progress"));
    }

    [Fact]
    public void Dado_Unlabeled_Quando_BuildTransitions_Entao_RegistraSaidaDaColuna()
    {
        // Covers RF-001: unlabeled on the current column → transition back to no-column
        var t1 = DateTimeOffset.UtcNow.AddDays(-8);
        var t2 = DateTimeOffset.UtcNow.AddDays(-3);
        var events = new List<IssueLabelEventDto>
        {
            new(t1, "todo", Added: true),
            new(t2, "todo", Added: false)
        };

        var transitions = TimelineMetricsService.BuildTransitions(events, null);

        transitions.Count.ShouldBe(2);
        transitions[1].ShouldBe(new IssueTransitionDto(t2, "todo", null));
    }

    [Fact]
    public void Dado_LabelsNaoColuna_Quando_BuildTransitions_Entao_Ignora()
    {
        // Covers RF-001: non-column labels (bug, priority:*) never produce transitions
        var events = new List<IssueLabelEventDto>
        {
            new(DateTimeOffset.UtcNow.AddDays(-4), "bug", Added: true),
            new(DateTimeOffset.UtcNow.AddDays(-3), "priority:high", Added: true)
        };

        TimelineMetricsService.BuildTransitions(events, null).ShouldBeEmpty();
    }

    [Fact]
    public void Dado_EventosLocais_Quando_BuildTransitions_Entao_MesclaEDeduplica()
    {
        // Covers RF-001: local ColumnMoved events merge with GitHub events,
        // deduplicated by exact (at, from, to)
        var at = DateTimeOffset.UtcNow.AddDays(-2);
        var events = new List<IssueLabelEventDto>
        {
            new(at, "in-progress", Added: true)
        };
        var local = new List<IssueHistoryEvent>
        {
            // exact duplicate of the replayed (at, null, "in-progress") — dedups
            new(Guid.NewGuid(), "1", "o/r", IssueHistoryEventKind.ColumnMoved, at,
                from: null, to: "in-progress"),
            // distinct local move — merged in
            new(Guid.NewGuid(), "1", "o/r", IssueHistoryEventKind.ColumnMoved,
                at.AddHours(1), from: "in-progress", to: "in-review")
        };

        var transitions = TimelineMetricsService.BuildTransitions(events, local);

        transitions.Count.ShouldBe(2);
        transitions[0].ShouldBe(new IssueTransitionDto(at, null, "in-progress"));
        transitions[1].ShouldBe(new IssueTransitionDto(
            at.AddHours(1), "in-progress", "in-review"));
    }

    [Fact]
    public void Dado_Metricas_Quando_ComputeMetrics_Entao_CalculaAgregados()
    {
        // Covers RF-002: lead time avg/median, cycle time, throughput, WIP, aging
        var now = DateTimeOffset.UtcNow;
        var timeline = new RepoTimelineDto(
        [
            new IssueTimelineDto(1, 1, "a", "done", "None", null,
                now.AddDays(-20), now.AddDays(-2), null, null,
                [new IssueTransitionDto(now.AddDays(-12), "todo", "in-progress")]),
            new IssueTimelineDto(2, 2, "b", "done", "None", null,
                now.AddDays(-10), now.AddDays(-1), null, null,
                [new IssueTransitionDto(now.AddDays(-8), null, "todo"),
                 new IssueTransitionDto(now.AddDays(-1), "in-progress", "done")]),
            new IssueTimelineDto(3, 3, "c", "in-progress", "None", "dev",
                now.AddDays(-5), null, null, null, [])
        ],
        []);

        var metrics = TimelineMetricsService.ComputeMetrics(timeline, 90);

        // lead: 18d e 9d → avg 13.5, median 13.5
        metrics.LeadTimeAvgDays!.Value.ShouldBe(13.5, 0.01);
        metrics.LeadTimeMedianDays!.Value.ShouldBe(13.5, 0.01);
        // cycle: a sai do backlog em -12d → done em -2d = 10d;
        // b sai em -8d → done em -1d = 7d → avg 8.5
        metrics.CycleTimeAvgDays!.Value.ShouldBe(8.5, 0.01);
        metrics.Wip.ShouldBe(1);
        metrics.OpenMedianAgeDays!.Value.ShouldBe(5, 0.01);
        metrics.ThroughputPerWeek.Count.ShouldBe(8);
        metrics.ThroughputPerWeek.Sum(p => p.Closed).ShouldBe(2);
    }

    [Fact]
    public void Dado_SemIssues_Quando_ComputeMetrics_Entao_MetricasZeradas()
    {
        // Covers AC5: empty repo → null aggregates, WIP 0, 8 empty weeks
        var metrics = TimelineMetricsService.ComputeMetrics(new RepoTimelineDto([], []), 90);

        metrics.LeadTimeAvgDays.ShouldBeNull();
        metrics.LeadTimeMedianDays.ShouldBeNull();
        metrics.CycleTimeAvgDays.ShouldBeNull();
        metrics.Wip.ShouldBe(0);
        metrics.OpenMedianAgeDays.ShouldBeNull();
        metrics.ThroughputPerWeek.Count.ShouldBe(8);
        metrics.ThroughputPerWeek.ShouldAllBe(p => p.Closed == 0);
    }

    [Fact]
    public async Task Dado_IssueForaDaJanela_Quando_GetTimeline_Entao_Exclui()
    {
        // Covers RF-003: issues closed before the window stay out
        var now = DateTimeOffset.UtcNow;
        var gitHub = Substitute.For<IGitHubService>();
        gitHub.GetIssuesAsync("o/r", Arg.Any<CancellationToken>()).Returns(
        [
            Issue(1, 1, now.AddDays(-200), now.AddDays(-120), GitHubBoardColumn.Done),
            Issue(2, 2, now.AddDays(-10), now.AddDays(-1), GitHubBoardColumn.Done)
        ]);
        gitHub.GetMilestonesAsync("o/r", Arg.Any<CancellationToken>())
            .Returns([]);
        gitHub.GetIssueTimelineEventsAsync("o/r", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var timeline = await CriarServico(gitHub).GetTimelineAsync("o", "r", 90);

        timeline.Issues.Count.ShouldBe(1);
        timeline.Issues[0].Number.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_TimelineFalhaNumaIssue_Quando_GetTimeline_Entao_DemaisCarregam()
    {
        // Covers AC4: a failing per-issue fetch leaves that issue without
        // transitions — the rest of the timeline still loads
        var now = DateTimeOffset.UtcNow;
        var gitHub = Substitute.For<IGitHubService>();
        gitHub.GetIssuesAsync("o/r", Arg.Any<CancellationToken>()).Returns(
        [
            Issue(1, 1, now.AddDays(-10)),
            Issue(2, 2, now.AddDays(-9))
        ]);
        gitHub.GetMilestonesAsync("o/r", Arg.Any<CancellationToken>())
            .Returns([new MilestoneDto(7, "v1", now.AddDays(10), "open")]);
        gitHub.GetIssueTimelineEventsAsync("o/r", 1, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("rate limit"));
        gitHub.GetIssueTimelineEventsAsync("o/r", 2, Arg.Any<CancellationToken>())
            .Returns([new IssueLabelEventDto(now.AddDays(-8), "todo", true)]);

        var timeline = await CriarServico(gitHub).GetTimelineAsync("o", "r", 90);

        timeline.Issues.Count.ShouldBe(2);
        timeline.Issues[0].Transitions.ShouldBeEmpty();
        timeline.Issues[1].Transitions.Count.ShouldBe(1);
        timeline.Milestones.Count.ShouldBe(1);
        timeline.Milestones[0].Number.ShouldBe(7);
    }

    [Fact]
    public async Task Dado_HistoricoLocal_Quando_GetTimeline_Entao_MesclaTransicoes()
    {
        // Covers RF-001: persisted ColumnMoved events join the GitHub replay
        var now = DateTimeOffset.UtcNow;
        _context.IssueHistoryEvents.Add(new IssueHistoryEvent(
            Guid.NewGuid(), "1", "o/r", IssueHistoryEventKind.ColumnMoved,
            now.AddDays(-6), from: "todo", to: "in-review"));
        await _context.SaveChangesAsync();

        var gitHub = Substitute.For<IGitHubService>();
        gitHub.GetIssuesAsync("o/r", Arg.Any<CancellationToken>()).Returns(
            [Issue(1, 1, now.AddDays(-10))]);
        gitHub.GetMilestonesAsync("o/r", Arg.Any<CancellationToken>()).Returns([]);
        gitHub.GetIssueTimelineEventsAsync("o/r", 1, Arg.Any<CancellationToken>()).Returns([]);

        var timeline = await CriarServico(gitHub).GetTimelineAsync("o", "r", 90);

        timeline.Issues.Single().Transitions.ShouldContain(
            t => t.From == "todo" && t.To == "in-review");
    }
}
