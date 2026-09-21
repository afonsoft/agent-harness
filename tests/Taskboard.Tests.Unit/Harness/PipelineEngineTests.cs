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
using Taskboard.Harness;
using Taskboard.Repositories;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

public class PipelineEngineTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<TaskboardDbContext> _options;
    private readonly TaskboardDbContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentAcpClient _acp = Substitute.For<IAgentAcpClient>();
    private readonly IWorkspaceIsolationService _isolation = Substitute.For<IWorkspaceIsolationService>();
    private readonly IVerificationEngine _verification = Substitute.For<IVerificationEngine>();

    public PipelineEngineTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-pipe-{Guid.NewGuid()}.sqlite");
        _options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(_options);
        _context.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddScoped(_ => new TaskboardDbContext(_options));
        services.AddScoped<IRepository<PipelineExecution>>(sp =>
            new EfCoreRepository<PipelineExecution>(sp.GetRequiredService<TaskboardDbContext>()));
        services.AddScoped(_ => _isolation);
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private PipelineEngine CriarEngine() =>
        new(_scopeFactory, _acp, _verification,
            NullLogger<PipelineEngine>.Instance);

    private PipelineExecution SalvarExecucao(PipelineDefinition def)
    {
        var exec = PipelineExecution.Create(
            def, "afonsoft/agent-harness", "/repo/taskboard", "main",
            "150", "Implementar JWT", DateTime.UtcNow);
        _context.PipelineExecutions.Add(exec);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
        return exec;
    }

    private PipelineExecution Recarregar(PipelineExecutionId id)
    {
        _context.ChangeTracker.Clear();
        return _context.PipelineExecutions.Include(e => e.Stages).Single(e => e.Id == id);
    }

    private string ConfigurarIsolacao()
    {
        var path = Path.Join(Path.GetTempPath(), $"tb-pipe-wt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Join(path, "App.sln"), "");
        _isolation.CreateWorktreeAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorktreeSessionDto(
                "wt-1", "run-1", path, "harness/run-1", "Active",
                "/repo/taskboard", "main", null, false,
                DateTime.UtcNow, DateTime.UtcNow, 1));
        return path;
    }

    [Fact]
    public async Task Dado_QuickPatch_Quando_Dispatch_Entao_BuilderDepoisVerifierCompletamPipeline()
    {
        var worktree = ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(0, true));
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(true, "Passed", [], null, 72.5, null));
        var exec = SalvarExecucao(PipelineTemplates.QuickPatch);
        var engine = CriarEngine();

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.Completed);
        final.Stages.ShouldAllBe(s => s.Status == StageStatus.Completed);
        final.WorktreePath.ShouldBe(worktree);
        await _acp.Received(1).ExecuteAsync(
            Arg.Is<AgentExecutionRequest>(r => r.RepoPath == worktree && r.AgentType == AgentType.Codex),
            Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_GateAprovacao_Quando_ArchitectCompleta_Entao_PipelineEsperaAprovacao()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var progress = call.ArgAt<IProgress<AgentLogMessage>>(1);
                progress.Report(new AgentLogMessage(DateTimeOffset.UtcNow, "150", AgentLogStream.StdOut, "PLANO: usar JWT bearer"));
                return Task.FromResult(new AgentExecutionResult(0, true));
            });
        var exec = SalvarExecucao(PipelineTemplates.StandardFeature);
        var engine = CriarEngine();

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        var meio = Recarregar(exec.Id);
        meio.Status.ShouldBe(PipelineStatus.WaitingApproval);
        meio.Stages.Single(s => s.StageKey == "approve-plan").Status.ShouldBe(StageStatus.WaitingApproval);
        meio.Stages.Single(s => s.StageKey == "architect").HandoffSummary.ShouldNotBeNull().ShouldContain("PLANO");
    }

    [Fact]
    public async Task Dado_PipelineEmEspera_Quando_AprovarEDispatch_Entao_BuilderRecebeHandoff()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var progress = call.ArgAt<IProgress<AgentLogMessage>>(1);
                progress.Report(new AgentLogMessage(DateTimeOffset.UtcNow, "150", AgentLogStream.StdOut, "PLANO: usar JWT bearer"));
                return Task.FromResult(new AgentExecutionResult(0, true));
            });
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(true, "Passed", [], null, 80, null));
        var exec = SalvarExecucao(PipelineTemplates.StandardFeature);
        var engine = CriarEngine();
        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        var meio = Recarregar(exec.Id);
        meio.ApproveStage("approve-plan", "ok", DateTime.UtcNow);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.Completed);
        await _acp.Received().ExecuteAsync(
            Arg.Is<AgentExecutionRequest>(r =>
                r.AgentType == AgentType.OpenCode && r.Instructions.Contains("PLANO")),
            Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_AgenteFalha_Quando_RetryEDispatch_Entao_StageReexecutaComPromptAjustado()
    {
        ConfigurarIsolacao();
        var chamadas = 0;
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new AgentExecutionResult(1, ++chamadas > 1)));
        var def = new PipelineDefinition("single", "Single",
        [
            new PipelineStage("builder", "Builder", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, []),
        ]);
        var exec = SalvarExecucao(def);
        var engine = CriarEngine();
        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        Recarregar(exec.Id).Status.ShouldBe(PipelineStatus.AwaitingRetry);
        var falho = Recarregar(exec.Id);
        falho.RetryStage("builder", "tente de outro jeito", DateTime.UtcNow);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.Completed);
        chamadas.ShouldBe(2);
        await _acp.Received().ExecuteAsync(
            Arg.Is<AgentExecutionRequest>(r => r.Instructions.Contains("tente de outro jeito")),
            Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_VerificacaoFalha_Quando_RunAsync_Entao_StageFailedComFeedback()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(0, true));
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(false, "TestsFailed", [],
                new TestSummaryDto(10, 9, 1, []), 60, "corrija o teste X"));
        var exec = SalvarExecucao(PipelineTemplates.QuickPatch);
        var engine = CriarEngine();

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.AwaitingRetry);
        var verifier = final.Stages.Single(s => s.StageKey == "verifier");
        verifier.Status.ShouldBe(StageStatus.Failed);
        verifier.LastError.ShouldNotBeNull().ShouldContain("corrija o teste X");
    }

    [Fact]
    public async Task Dado_DoisEstagiosParalelos_Quando_DependenciaCompleta_Entao_AmbosDespachados()
    {
        ConfigurarIsolacao();
        var agentes = new List<AgentType>();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (agentes)
                {
                    agentes.Add(call.ArgAt<AgentExecutionRequest>(0).AgentType);
                }
                return Task.FromResult(new AgentExecutionResult(0, true));
            });
        var def = new PipelineDefinition("par", "Par",
        [
            new PipelineStage("plan", "Plan", PipelineStageKind.AgentWork,
                AgentRole.Architect, AgentType.Claude, AgentModelTier.Normal, []),
            new PipelineStage("front", "Front", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, ["plan"]),
            new PipelineStage("back", "Back", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.OpenCode, AgentModelTier.Normal, ["plan"]),
        ]);
        var exec = SalvarExecucao(def);
        var engine = CriarEngine();

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        agentes[0].ShouldBe(AgentType.Claude);
        agentes.Skip(1).Order().ShouldBe([AgentType.Codex, AgentType.OpenCode]);
        Recarregar(exec.Id).Status.ShouldBe(PipelineStatus.Completed);
    }
}
