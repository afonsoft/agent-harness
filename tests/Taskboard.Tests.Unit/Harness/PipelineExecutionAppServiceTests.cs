using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Application.Harness;
using Taskboard.Domain.Entities.Harness;
using Taskboard.Dtos;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.EntityFrameworkCore.Repositories;
using Taskboard.GitHub;
using Taskboard.Harness;
using Taskboard.Repositories;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20260919-ade-cockpit-hitl RF-005: after the run's PR is created, a run
/// bound to a board issue moves the card to <c>in_review</c> and comments the
/// PR link on the issue.
/// </summary>
public class PipelineExecutionAppServiceTests : IDisposable
{
    private const string PrUrl = "https://github.com/afonsoft/agent-harness/pull/77";

    private readonly string _dbPath;
    private readonly DbContextOptions<TaskboardDbContext> _options;
    private readonly TaskboardDbContext _context;
    private readonly IRepository<PipelineExecution> _repo;
    private readonly IWorkspaceIsolationService _isolation = Substitute.For<IWorkspaceIsolationService>();
    private readonly IGitHubService _gitHub = Substitute.For<IGitHubService>();
    private readonly PipelineExecutionAppService _sut;

    public PipelineExecutionAppServiceTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-pipe-svc-{Guid.NewGuid()}.sqlite");
        _options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(_options);
        _context.Database.EnsureCreated();
        _repo = new EfCoreRepository<PipelineExecution>(_context);

        var services = new ServiceCollection();
        services.AddScoped(_ => new TaskboardDbContext(_options));
        services.AddScoped<IRepository<PipelineExecution>>(sp =>
            new EfCoreRepository<PipelineExecution>(sp.GetRequiredService<TaskboardDbContext>()));
        services.AddScoped(_ => Substitute.For<IWorkspaceIsolationService>());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var engine = new PipelineEngine(
            scopeFactory,
            Substitute.For<IAgentAcpClient>(),
            Substitute.For<IVerificationEngine>(),
            NullLogger<PipelineEngine>.Instance);

        _sut = new PipelineExecutionAppService(
            _repo,
            engine,
            _isolation,
            _gitHub,
            NullLogger<PipelineExecutionAppService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private PipelineExecution SalvarExecucaoConcluida(string? issueId = "77")
    {
        var exec = PipelineExecution.Create(
            PipelineTemplates.SingleAgent, "afonsoft/agent-harness", "/repo/taskboard", "main",
            issueId, "Implementar JWT", DateTime.UtcNow);
        var now = DateTime.UtcNow;
        foreach (var stage in exec.Stages)
        {
            exec.MarkStageRunning(stage.StageKey, now);
            exec.CompleteStage(stage.StageKey, "done", now);
        }

        _context.PipelineExecutions.Add(exec);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
        return exec;
    }

    private void ConfigurarWorktreeEGitHub()
    {
        _isolation.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WorktreeSessionDto(
                "wt-1", "run-1", "/tmp/wt", "harness/run-1", "Active",
                "/repo/taskboard", "main", null, false,
                DateTime.UtcNow, DateTime.UtcNow, 1));
        _isolation.GetDiffAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WorkspaceDiffDto(0, 0, 0, [], ""));
        _isolation.PushAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("harness/run-1");
        _gitHub.CreatePullRequestAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(PrUrl);
        _gitHub.GetIssuesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([
                new IssueDto(77, 5, "itest issue", null, "open", "u", "h",
                    ["in-progress"], GitHubBoardColumn.InProgress, null, "None",
                    DateTimeOffset.UtcNow, null, null)
            ]);
    }

    [Fact]
    public async Task Dado_RunCompletadaVinculadaAIssue_Quando_CreatePr_Entao_CardInReviewEComentarioComLink()
    {
        ConfigurarWorktreeEGitHub();
        var exec = SalvarExecucaoConcluida();

        var prUrl = await _sut.CreatePullRequestAsync(exec.Id.Value, "feat: jwt", null, CancellationToken.None);

        prUrl.ShouldBe(PrUrl);
        await _gitHub.Received(1).UpdateIssueColumnAsync(
            "afonsoft/agent-harness", 5, GitHubBoardColumn.InProgress,
            GitHubBoardColumn.InReview, Arg.Any<CancellationToken>());
        await _gitHub.Received(1).AddIssueCommentAsync(
            "afonsoft/agent-harness", 5,
            Arg.Is<string>(body => body.Contains(PrUrl)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RunCompletadaSemIssue_Quando_CreatePr_Entao_NaoTocaNoBoard()
    {
        ConfigurarWorktreeEGitHub();
        var exec = SalvarExecucaoConcluida(issueId: null);

        var prUrl = await _sut.CreatePullRequestAsync(exec.Id.Value, "feat: jwt", null, CancellationToken.None);

        prUrl.ShouldBe(PrUrl);
        await _gitHub.DidNotReceive().UpdateIssueColumnAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<GitHubBoardColumn?>(),
            Arg.Any<GitHubBoardColumn>(), Arg.Any<CancellationToken>());
        await _gitHub.DidNotReceive().AddIssueCommentAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_IssueForaDoBoard_Quando_CreatePr_Entao_PrCriadoMesmoAssim()
    {
        // Issue não resolvida (fechada/fora da janela de visibilidade) é
        // best-effort — o PR já existe e a request não pode falhar.
        ConfigurarWorktreeEGitHub();
        _gitHub.GetIssuesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var exec = SalvarExecucaoConcluida();

        var prUrl = await _sut.CreatePullRequestAsync(exec.Id.Value, "feat: jwt", null, CancellationToken.None);

        prUrl.ShouldBe(PrUrl);
    }

    [Fact]
    public async Task Dado_RunNaoCompletada_Quando_CreatePr_Entao_InvalidPipelineState()
    {
        var exec = PipelineExecution.Create(
            PipelineTemplates.SingleAgent, "afonsoft/agent-harness", "/repo/taskboard", "main",
            "77", "Implementar JWT", DateTime.UtcNow);
        _context.PipelineExecutions.Add(exec);
        _context.SaveChanges();

        var ex = await Should.ThrowAsync<DomainException>(
            () => _sut.CreatePullRequestAsync(exec.Id.Value, "feat: jwt", null, CancellationToken.None));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidPipelineState);
        await _gitHub.DidNotReceive().CreatePullRequestAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RunSemWorktree_Quando_CreatePr_Entao_InvalidPipelineState()
    {
        var exec = SalvarExecucaoConcluida();
        _isolation.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((WorktreeSessionDto?)null);

        var ex = await Should.ThrowAsync<DomainException>(
            () => _sut.CreatePullRequestAsync(exec.Id.Value, "feat: jwt", null, CancellationToken.None));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidPipelineState);
    }

    [Fact]
    public async Task Dado_RunInexistente_Quando_CreatePr_Entao_Null()
    {
        var prUrl = await _sut.CreatePullRequestAsync("run-inexistente", "feat: jwt", null, CancellationToken.None);

        prUrl.ShouldBeNull();
    }
}
