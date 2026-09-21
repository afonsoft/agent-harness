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

/// <summary>
/// SPEC-20260919-ade-cockpit-hitl RF-001/RF-003/RF-004 — o engine publica
/// eventos estruturados no cockpit stream e dobra instruções de steer na
/// próxima etapa despachada.
/// </summary>
public class PipelineCockpitTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<TaskboardDbContext> _options;
    private readonly TaskboardDbContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentAcpClient _acp = Substitute.For<IAgentAcpClient>();
    private readonly IWorkspaceIsolationService _isolation = Substitute.For<IWorkspaceIsolationService>();
    private readonly IVerificationEngine _verification = Substitute.For<IVerificationEngine>();
    private readonly ICockpitEventStream _cockpit = Substitute.For<ICockpitEventStream>();
    private readonly ISteerQueue _steer = Substitute.For<ISteerQueue>();

    public PipelineCockpitTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-cockpit-{Guid.NewGuid()}.sqlite");
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
            NullLogger<PipelineEngine>.Instance, _cockpit, _steer);

    private PipelineExecution SalvarExecucao(PipelineDefinition def)
    {
        var exec = PipelineExecution.Create(
            def, "afonsoft/taskboard-ai", "/repo/taskboard", "main",
            "150", "Implementar JWT", DateTime.UtcNow);
        _context.PipelineExecutions.Add(exec);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
        return exec;
    }

    private void ConfigurarIsolacao()
    {
        var path = Path.Join(Path.GetTempPath(), $"tb-cockpit-wt-{Guid.NewGuid():N}");
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

    [Fact]
    public async Task Dado_StageAgente_Quando_Dispatch_Entao_PublicaStartedOutputECompleted()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.ArgAt<IProgress<AgentLogMessage>>(1).Report(
                    new AgentLogMessage(DateTimeOffset.UtcNow, "150", AgentLogStream.StdOut, "pensando em JWT"));
                return Task.FromResult(new AgentExecutionResult(0, true));
            });
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(true, "Passed", [], null, 80, null));
        var exec = SalvarExecucao(PipelineTemplates.QuickPatch);
        var engine = CriarEngine();

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        await _cockpit.Received().PublishAsync(
            Arg.Is<CockpitEventDto>(e =>
                e.RunId == exec.Id.Value && e.Kind == "stage" && e.Title.Contains("started")),
            Arg.Any<CancellationToken>());
        await _cockpit.Received().PublishAsync(
            Arg.Is<CockpitEventDto>(e => e.Kind == "agent_output" && e.PayloadJson!.Contains("pensando em JWT")),
            Arg.Any<CancellationToken>());
        await _cockpit.Received().PublishAsync(
            Arg.Is<CockpitEventDto>(e => e.Kind == "verification" && e.Title.Contains("passed")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_GateAprovacao_Quando_Dispatch_Entao_PublicaRequireApprovalComStagePrefix()
    {
        ConfigurarIsolacao();
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(0, true));
        var exec = SalvarExecucao(PipelineTemplates.StandardFeature);
        var engine = CriarEngine();

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        await _cockpit.Received().PublishApprovalAsync(
            Arg.Is<ApprovalRequestDto>(a =>
                a.RunId == exec.Id.Value
                && a.RequestId == "stage:approve-plan"
                && a.Options.Contains("Allow")
                && a.Options.Contains("Deny")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_SteerEnfileirado_Quando_ProximoStage_Entao_InstrucaoNoPrompt()
    {
        ConfigurarIsolacao();
        _steer.Drain(Arg.Any<string>()).Returns(["use refresh token também"]);
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(0, true));
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(true, "Passed", [], null, 80, null));
        var exec = SalvarExecucao(PipelineTemplates.QuickPatch);
        var engine = CriarEngine();

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        _steer.Received().Drain(exec.Id.Value);
        await _acp.Received().ExecuteAsync(
            Arg.Is<AgentExecutionRequest>(r => r.Instructions.Contains("use refresh token também")),
            Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_SemSteer_Quando_Dispatch_Entao_PromptSemSecaoDeSteer()
    {
        ConfigurarIsolacao();
        _steer.Drain(Arg.Any<string>()).Returns([]);
        _acp.ExecuteAsync(Arg.Any<AgentExecutionRequest>(), Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new AgentExecutionResult(0, true));
        _verification.RunAsync(Arg.Any<VerificationRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new VerificationReportDto(true, "Passed", [], null, 80, null));
        SalvarExecucao(PipelineTemplates.QuickPatch);
        var engine = CriarEngine();

        await engine.DispatchPendingAsync();
        await engine.DrainAsync();

        await _acp.Received().ExecuteAsync(
            Arg.Is<AgentExecutionRequest>(r => !r.Instructions.Contains("Human steer")),
            Arg.Any<IProgress<AgentLogMessage>>(), Arg.Any<CancellationToken>());
    }
}
