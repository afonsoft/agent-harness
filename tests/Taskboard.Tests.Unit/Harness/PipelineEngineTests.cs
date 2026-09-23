using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
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

    /// <summary>Retry imediato (intervalo zero) com 2 falhas por CLI — usado pelos testes de rotação.</summary>
    private static readonly PipelineAutoRetryOptions Imediato = new()
    {
        Enabled = true,
        AttemptsPerAgent = 2,
        Interval = TimeSpan.Zero,
    };

    private PipelineEngine CriarEngine(
        IAgentExecutionEventSink? eventSink = null, PipelineAutoRetryOptions? autoRetry = null) =>
        new(_scopeFactory, _acp, _verification,
            NullLogger<PipelineEngine>.Instance, eventSink: eventSink, autoRetry: autoRetry);

    /// <summary>
    /// Engine com <see cref="IAgentEligibilityService"/> no escopo — habilita a
    /// rotação de CLIs do auto-retry (SPEC-20260923-cockpit-run-hardening RF-002).
    /// </summary>
    private PipelineEngine CriarEngineComElegiveis(
        IReadOnlySet<AgentType> elegiveis, PipelineAutoRetryOptions? autoRetry = null)
    {
        var eligibility = Substitute.For<IAgentEligibilityService>();
        eligibility.GetEligibleTypesAsync(Arg.Any<CancellationToken>()).Returns(elegiveis);
        var services = new ServiceCollection();
        services.AddScoped(_ => new TaskboardDbContext(_options));
        services.AddScoped<IRepository<PipelineExecution>>(sp =>
            new EfCoreRepository<PipelineExecution>(sp.GetRequiredService<TaskboardDbContext>()));
        services.AddScoped(_ => _isolation);
        services.AddScoped(_ => eligibility);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new PipelineEngine(
            scopeFactory, _acp, _verification, NullLogger<PipelineEngine>.Instance,
            autoRetry: autoRetry);
    }

    /// <summary>
    /// Ticks até a execução chegar num estado terminal (ou estourar o limite) —
    /// com <see cref="PipelineAutoRetryOptions.Interval"/> zero cada varredura
    /// agenda e dispara a próxima tentativa.
    /// </summary>
    private async Task RodarAteTerminal(PipelineEngine engine, PipelineExecutionId id, int maxTicks = 12)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            await engine.DispatchPendingAsync();
            await engine.DrainAsync();
            var status = Recarregar(id).Status;
            if (status is PipelineStatus.Completed or PipelineStatus.Failed or PipelineStatus.Cancelled)
            {
                return;
            }
        }
    }

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

    [Fact]
    public async Task Dado_CliEsgotaTentativas_Quando_OutroElegivel_Entao_StageCompletaComRotacao()
    {
        // SPEC-20260923 RF-002: o CLI vinculado retenta até o budget por agente;
        // esgotado, a varredura rotaciona para o próximo elegível não-tentado.
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new AgentExecutionResult(
                call.ArgAt<AgentExecutionRequest>(0).AgentType == AgentType.Codex ? 1 : 0,
                call.ArgAt<AgentExecutionRequest>(0).AgentType != AgentType.Codex)));
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(true, "Passed", [], null, 80, null));
        var exec = SalvarExecucao(PipelineTemplates.QuickPatch);
        var engine = CriarEngineComElegiveis(
            new HashSet<AgentType> { AgentType.Codex, AgentType.OpenCode }, Imediato);

        await RodarAteTerminal(engine, exec.Id);

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.Completed);
        var builder = final.Stages.Single(s => s.StageKey == "builder");
        builder.Status.ShouldBe(StageStatus.Completed);
        builder.Agent.ShouldBe(AgentType.OpenCode);
        builder.Attempts.ShouldBe(3);
        builder.TriedAgents.ShouldBe([nameof(AgentType.Codex)]);
    }

    [Fact]
    public async Task Dado_ModeloRejeitado_Quando_UnrecognizedModel_Entao_RetentaSemFlagNoMesmoCli()
    {
        // SPEC-20260922 RF-004: `unrecognized_model` retenta uma vez no mesmo
        // CLI sem model flag antes de consumir o próximo candidato.
        ConfigurarIsolacao();
        var chamadas = new List<AgentExecutionRequest>();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.ArgAt<AgentExecutionRequest>(0);
                chamadas.Add(request);
                if (chamadas.Count == 1)
                {
                    call.ArgAt<IProgress<AgentLogMessage>>(1).Report(new AgentLogMessage(
                        DateTimeOffset.UtcNow, "150", AgentLogStream.StdErr,
                        "\"Opus\" isn't described by this version's model catalog [unrecognized_model]"));
                    return Task.FromResult(new AgentExecutionResult(1, false));
                }

                return Task.FromResult(new AgentExecutionResult(0, true));
            });
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(true, "Passed", [], null, 80, null));
        var exec = SalvarExecucao(PipelineTemplates.QuickPatch);
        var engine = CriarEngineComElegiveis(new HashSet<AgentType> { AgentType.Codex, AgentType.OpenCode });

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.Completed);
        var builder = final.Stages.Single(s => s.StageKey == "builder");
        builder.Agent.ShouldBe(AgentType.Codex);
        chamadas.Count.ShouldBe(2);
        chamadas.ShouldAllBe(r => r.AgentType == AgentType.Codex);
        chamadas[0].OmitModelFlag.ShouldBeFalse();
        chamadas[1].OmitModelFlag.ShouldBeTrue();
        chamadas[1].ResolvedModelName.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_TodosClisFalham_Quando_EsgotaCadeia_Entao_RunFailedComMotivo()
    {
        // SPEC-20260923 RF-002: sem CLI elegível restante a execução termina em
        // Failed com o detalhe (stage, agents tentados, último erro).
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(1, false));
        var exec = SalvarExecucao(PipelineTemplates.QuickPatch);
        var engine = CriarEngineComElegiveis(
            new HashSet<AgentType> { AgentType.Codex, AgentType.OpenCode },
            new PipelineAutoRetryOptions
            {
                Enabled = true,
                AttemptsPerAgent = 1,
                Interval = TimeSpan.Zero,
            });

        await RodarAteTerminal(engine, exec.Id);

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.Failed);
        var reason = final.FailureReason.ShouldNotBeNull();
        reason.ShouldContain("builder");
        reason.ShouldContain("Codex");
        reason.ShouldContain("OpenCode");
        reason.ShouldContain("exited with code 1");
        var builder = final.Stages.Single(s => s.StageKey == "builder");
        builder.Status.ShouldBe(StageStatus.Failed);
        builder.TriedAgents.ShouldBe([nameof(AgentType.Codex), nameof(AgentType.OpenCode)]);
        builder.Attempts.ShouldBe(2);
        // O verifier nunca foi despachado — a DAG morreu no builder.
        final.Stages.Single(s => s.StageKey == "verifier").Status.ShouldBe(StageStatus.Skipped);
    }

    [Fact]
    public async Task Dado_CliInelegivelNoDispatch_Quando_Roda_Entao_RotacionaParaElegivel()
    {
        // Elegibilidade é dinâmica — o CLI vinculado desabilitado falha a
        // tentativa sem rodar e a varredura rotaciona para o elegível.
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(0, true));
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(true, "Passed", [], null, 80, null));
        var exec = SalvarExecucao(PipelineTemplates.QuickPatch); // builder → Codex
        var engine = CriarEngineComElegiveis(new HashSet<AgentType> { AgentType.OpenCode }, Imediato);

        await RodarAteTerminal(engine, exec.Id);

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.Completed);
        final.Stages.Single(s => s.StageKey == "builder").Agent.ShouldBe(AgentType.OpenCode);
        await _acp.DidNotReceive().ExecuteAsync(
            Arg.Is<AgentExecutionRequest>(r => r.AgentType == AgentType.Codex),
            Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_SpawnLancaExcecao_Quando_EsgotaTentativas_Entao_RotacionaParaProximoCli()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<AgentExecutionRequest>(0).AgentType == AgentType.Codex
                ? Task.FromException<AgentExecutionResult>(new FileNotFoundException("Executable 'codex' not found in PATH."))
                : Task.FromResult(new AgentExecutionResult(0, true)));
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(true, "Passed", [], null, 80, null));
        var exec = SalvarExecucao(PipelineTemplates.QuickPatch);
        var engine = CriarEngineComElegiveis(
            new HashSet<AgentType> { AgentType.Codex, AgentType.OpenCode }, Imediato);

        await RodarAteTerminal(engine, exec.Id);

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.Completed);
        var builder = final.Stages.Single(s => s.StageKey == "builder");
        builder.Agent.ShouldBe(AgentType.OpenCode);
        builder.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RetryManual_Quando_AposFalha_Entao_TriedAgentsEAutoRetryResetam()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(1, false));
        var def = new PipelineDefinition("single", "Single",
        [
            new PipelineStage("builder", "Builder", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, []),
        ]);
        var exec = SalvarExecucao(def);
        var engine = CriarEngineComElegiveis(
            new HashSet<AgentType> { AgentType.Codex, AgentType.OpenCode }, Imediato);
        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        var falho = Recarregar(exec.Id);
        var builder = falho.Stages.Single(s => s.StageKey == "builder");
        builder.TriedAgents.ShouldBe([nameof(AgentType.Codex)]);
        builder.AutoRetryCount.ShouldBe(1);
        builder.NextAutoRetryAtUtc.ShouldNotBeNull();

        falho.RetryStage("builder", null, DateTime.UtcNow);
        _context.SaveChanges();

        var reset = Recarregar(exec.Id).Stages.Single(s => s.StageKey == "builder");
        reset.TriedAgents.ShouldBeEmpty();
        reset.AutoRetryCount.ShouldBe(0);
        reset.NextAutoRetryAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_ExecucaoVinculadaAIssue_Quando_ProgressoDoAgente_Entao_EmiteNosEscoposRunEIssue()
    {
        // SPEC-20260921-board-cockpit-agent-observability RF-002: the Board task
        // log reads issue:{issueId} — pipeline events must mirror run-scoped
        // emissions to the bound issue scope.
        ConfigurarIsolacao();
        var emitted = new List<AgentExecutionEvent>();
        var sink = Substitute.For<IAgentExecutionEventSink>();
        sink.EmitAsync(Arg.Any<AgentExecutionEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var evt = call.ArgAt<AgentExecutionEvent>(0);
                lock (emitted)
                {
                    emitted.Add(evt);
                }
                return Task.FromResult(evt);
            });
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var progress = call.ArgAt<IProgress<AgentLogMessage>>(1);
                progress.Report(new AgentLogMessage(DateTimeOffset.UtcNow, "150", AgentLogStream.StdOut, "trabalhando no diff"));
                return Task.FromResult(new AgentExecutionResult(0, true));
            });
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(true, "Passed", [], null, 80, null));
        var exec = SalvarExecucao(PipelineTemplates.QuickPatch);
        var engine = CriarEngine(sink);

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        List<AgentExecutionEvent> snapshot;
        lock (emitted)
        {
            snapshot = [.. emitted];
        }
        snapshot.ShouldContain(e => e.ScopeKind == AgentEventScope.Run && e.ScopeId == exec.Id.Value);
        snapshot.ShouldContain(e => e.ScopeKind == AgentEventScope.Issue && e.ScopeId == "150");
        // The mirrored issue event preserves the normalized payload of the run event.
        var runOutput = snapshot.First(e => e.ScopeKind == AgentEventScope.Run
            && e.PayloadJson is not null && e.PayloadJson.Contains("trabalhando no diff"));
        snapshot.ShouldContain(e => e.ScopeKind == AgentEventScope.Issue
            && e.ScopeId == "150" && e.Kind == runOutput.Kind && e.PayloadJson == runOutput.PayloadJson);
    }
}
