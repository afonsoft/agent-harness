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

/// <summary>
/// SPEC-20260923-cockpit-run-hardening RF-002 — auto-retry persistido:
/// N falhas por CLI espaçadas por <see cref="PipelineAutoRetryOptions.Interval"/>,
/// rotação para o próximo CLI elegível não-tentado e falha terminal quando
/// esgotam os candidatos.
/// </summary>
public class PipelineAutoRetryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<TaskboardDbContext> _options;
    private readonly TaskboardDbContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentAcpClient _acp = Substitute.For<IAgentAcpClient>();
    private readonly IWorkspaceIsolationService _isolation = Substitute.For<IWorkspaceIsolationService>();
    private readonly IVerificationEngine _verification = Substitute.For<IVerificationEngine>();

    private static readonly PipelineAutoRetryOptions Imediato = new()
    {
        Enabled = true,
        AttemptsPerAgent = 2,
        Interval = TimeSpan.Zero,
    };

    public PipelineAutoRetryTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-retry-{Guid.NewGuid()}.sqlite");
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

    private PipelineEngine CriarEngine(
        IReadOnlySet<AgentType>? elegiveis = null,
        PipelineAutoRetryOptions? autoRetry = null,
        IAgentExecutionEventSink? eventSink = null)
    {
        if (elegiveis is null)
        {
            return new PipelineEngine(_scopeFactory, _acp, _verification,
                NullLogger<PipelineEngine>.Instance, eventSink: eventSink, autoRetry: autoRetry);
        }

        var eligibility = Substitute.For<IAgentEligibilityService>();
        eligibility.GetEligibleTypesAsync(Arg.Any<CancellationToken>()).Returns(elegiveis);
        var services = new ServiceCollection();
        services.AddScoped(_ => new TaskboardDbContext(_options));
        services.AddScoped<IRepository<PipelineExecution>>(sp =>
            new EfCoreRepository<PipelineExecution>(sp.GetRequiredService<TaskboardDbContext>()));
        services.AddScoped(_ => _isolation);
        services.AddScoped(_ => eligibility);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new PipelineEngine(scopeFactory, _acp, _verification,
            NullLogger<PipelineEngine>.Instance, eventSink: eventSink, autoRetry: autoRetry);
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

    private static PipelineDefinition SingleAgent() =>
        new("single", "Single",
        [
            new PipelineStage("builder", "Builder", PipelineStageKind.AgentWork,
                AgentRole.Builder, AgentType.Codex, AgentModelTier.Normal, []),
        ]);

    private void ConfigurarIsolacao()
    {
        var path = Path.Join(Path.GetTempPath(), $"tb-retry-wt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Join(path, "App.sln"), "");
        _isolation.CreateWorktreeAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorktreeSessionDto(
                "wt-1", "run-1", path, "harness/run-1", "Active",
                "/repo/taskboard", "main", null, false,
                DateTime.UtcNow, DateTime.UtcNow, 1));
    }

    private async Task RodarAteTerminal(PipelineEngine engine, PipelineExecutionId id, int maxTicks = 20)
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

    [Fact]
    public async Task Dado_Falha_Quando_Varredura_Entao_AgendaRetryComIntervaloSemDespachar()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(1, false));
        var exec = SalvarExecucao(SingleAgent());
        var engine = CriarEngine(autoRetry: new PipelineAutoRetryOptions
        {
            Enabled = true,
            AttemptsPerAgent = 5,
            Interval = TimeSpan.FromMinutes(1),
        });

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        var agendado = Recarregar(exec.Id).Stages.Single(s => s.StageKey == "builder");
        agendado.Status.ShouldBe(StageStatus.Failed);
        agendado.AutoRetryCount.ShouldBe(1);
        agendado.NextAutoRetryAtUtc.ShouldNotBeNull();
        (agendado.NextAutoRetryAtUtc.Value - DateTime.UtcNow).ShouldBeInRange(
            TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));

        // Antes do vencimento nenhuma tentativa extra é despachada.
        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        await _acp.Received(1).ExecuteAsync(
            Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>());
        Recarregar(exec.Id).Status.ShouldBe(PipelineStatus.AwaitingRetry);
    }

    [Fact]
    public async Task Dado_RetryVencido_Quando_Tick_Entao_RedespachaMesmoCli()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(1, false));
        var exec = SalvarExecucao(SingleAgent());
        var engine = CriarEngine(autoRetry: Imediato);

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();
        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        var builder = Recarregar(exec.Id).Stages.Single(s => s.StageKey == "builder");
        builder.Attempts.ShouldBe(2);
        builder.Agent.ShouldBe(AgentType.Codex);
        await _acp.Received(2).ExecuteAsync(
            Arg.Is<AgentExecutionRequest>(r => r.AgentType == AgentType.Codex),
            Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_BudgetDoCliEsgotado_Quando_Tick_Entao_RotacionaEResetaContador()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new AgentExecutionResult(
                call.ArgAt<AgentExecutionRequest>(0).AgentType == AgentType.Codex ? 1 : 0,
                call.ArgAt<AgentExecutionRequest>(0).AgentType != AgentType.Codex)));
        var exec = SalvarExecucao(SingleAgent());
        var engine = CriarEngineComElegiveisFallback(new HashSet<AgentType> { AgentType.Codex, AgentType.OpenCode });

        await RodarAteTerminal(engine, exec.Id);

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.Completed);
        var builder = final.Stages.Single(s => s.StageKey == "builder");
        builder.Agent.ShouldBe(AgentType.OpenCode);
        builder.Attempts.ShouldBe(3);
        builder.TriedAgents.ShouldBe([nameof(AgentType.Codex)]);
    }

    [Fact]
    public async Task Dado_TodosCandidatosEsgotados_Quando_Tick_Entao_FailedTerminalComMotivo()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(7, false));
        var exec = SalvarExecucao(SingleAgent());
        var engine = CriarEngineComElegiveisFallback(new HashSet<AgentType> { AgentType.Codex, AgentType.OpenCode });

        await RodarAteTerminal(engine, exec.Id);

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.Failed);
        final.CompletedAtUtc.ShouldNotBeNull();
        var reason = final.FailureReason.ShouldNotBeNull();
        reason.ShouldContain("builder");
        reason.ShouldContain("Codex");
        reason.ShouldContain("OpenCode");
        reason.ShouldContain("exited with code 7");
        var builder = final.Stages.Single(s => s.StageKey == "builder");
        builder.Attempts.ShouldBe(4); // 2 por agente
        builder.TriedAgents.ShouldBe([nameof(AgentType.Codex), nameof(AgentType.OpenCode)]);
    }

    [Fact]
    public async Task Dado_RunFailedTerminal_Quando_Tick_Entao_NenhumNovoDespacho()
    {
        ConfigurarIsolacao();
        var chamadas = 0;
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                chamadas++;
                return Task.FromResult(new AgentExecutionResult(1, false));
            });
        var exec = SalvarExecucao(SingleAgent());
        var engine = CriarEngineComElegiveisFallback(new HashSet<AgentType> { AgentType.Codex });

        await RodarAteTerminal(engine, exec.Id);
        Recarregar(exec.Id).Status.ShouldBe(PipelineStatus.Failed);
        var total = chamadas;

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        chamadas.ShouldBe(total);
    }

    [Fact]
    public async Task Dado_AutoRetryDesabilitado_Quando_Falha_Entao_FicaAwaitingRetrySemAgenda()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(1, false));
        var exec = SalvarExecucao(SingleAgent());
        var engine = CriarEngine(autoRetry: new PipelineAutoRetryOptions { Enabled = false });

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();
        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.AwaitingRetry);
        var builder = final.Stages.Single(s => s.StageKey == "builder");
        builder.AutoRetryCount.ShouldBe(0);
        builder.NextAutoRetryAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_VerificacaoFalha_Quando_EsgotaBudget_Entao_FailedSemRotacao()
    {
        // Stages sem agente (Verification) não rotacionam — o mesmo step retenta
        // até o budget e derruba a execução.
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(0, true));
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(false, "TestsFailed", [],
                new TestSummaryDto(10, 9, 1, []), 60, "corrija o teste X"));
        var exec = SalvarExecucao(PipelineTemplates.QuickPatch);
        var engine = CriarEngine(autoRetry: Imediato);

        await RodarAteTerminal(engine, exec.Id);

        var final = Recarregar(exec.Id);
        final.Status.ShouldBe(PipelineStatus.Failed);
        final.FailureReason.ShouldNotBeNull().ShouldContain("verifier");
        var verifier = final.Stages.Single(s => s.StageKey == "verifier");
        verifier.Status.ShouldBe(StageStatus.Failed);
        verifier.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_FalhaDeAgente_Quando_Registra_Entao_EmiteEventoErrorComExitCode()
    {
        ConfigurarIsolacao();
        var emitted = new List<AgentExecutionEvent>();
        var sink = Substitute.For<IAgentExecutionEventSink>();
        sink.EmitAsync(Arg.Any<AgentExecutionEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (emitted)
                {
                    emitted.Add(call.ArgAt<AgentExecutionEvent>(0));
                }
                return Task.FromResult(call.ArgAt<AgentExecutionEvent>(0));
            });
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(42, false));
        var exec = SalvarExecucao(SingleAgent());
        var engine = CriarEngine(autoRetry: Imediato, eventSink: sink);

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        List<AgentExecutionEvent> snapshot;
        lock (emitted)
        {
            snapshot = [.. emitted];
        }
        snapshot.ShouldContain(e =>
            e.ScopeKind == AgentEventScope.Run
            && e.ScopeId == exec.Id.Value
            && e.Kind == AgentEventKinds.Error
            && e.StageId == "builder"
            && e.PayloadJson != null && e.PayloadJson.Contains("\"exitCode\":42"));
    }

    private PipelineEngine CriarEngineComElegiveisFallback(IReadOnlySet<AgentType> elegiveis) =>
        CriarEngine(elegiveis, Imediato);
}
