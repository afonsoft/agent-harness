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
using Taskboard.GitHub;
using Taskboard.Harness;
using Taskboard.Repositories;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20260922-cockpit-agent-selection-fallback RF-001/RF-002 — per-stage
/// overrides + Single Agent em qualquer template, e resolução Auto contra o
/// conjunto de CLIs elegíveis no start.
/// </summary>
public class CockpitAgentSelectionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<TaskboardDbContext> _options;
    private readonly TaskboardDbContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentEligibilityService _eligibility = Substitute.For<IAgentEligibilityService>();

    public CockpitAgentSelectionTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-sel-{Guid.NewGuid()}.sqlite");
        _options = new DbContextOptionsBuilder<TaskboardDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=false")
            .Options;
        _context = new TaskboardDbContext(_options);
        _context.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddScoped(_ => new TaskboardDbContext(_options));
        services.AddScoped<IRepository<PipelineExecution>>(sp =>
            new EfCoreRepository<PipelineExecution>(sp.GetRequiredService<TaskboardDbContext>()));
        services.AddScoped(_ => Substitute.For<IWorkspaceIsolationService>());
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        TodosElegiveis();
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private void TodosElegiveis() =>
        _eligibility.GetEligibleTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<AgentType>(Enum.GetValues<AgentType>()));

    private void Elegiveis(params AgentType[] types) =>
        _eligibility.GetEligibleTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<AgentType>(types));

    private PipelineExecutionAppService CriarServico() =>
        new(
            new EfCoreRepository<PipelineExecution>(_context),
            new PipelineEngine(
                _scopeFactory,
                Substitute.For<IAgentAcpClient>(),
                Substitute.For<IVerificationEngine>(),
                NullLogger<PipelineEngine>.Instance),
            Substitute.For<IWorkspaceIsolationService>(),
            Substitute.For<IGitHubService>(),
            NullLogger<PipelineExecutionAppService>.Instance,
            _eligibility);

    private static PipelineStartRequest CriarRequest(
        string templateId = "standard-feature",
        IReadOnlyDictionary<string, PipelineStageOverrideDto>? stageOverrides = null,
        bool singleAgent = false,
        AgentType? singleAgentType = null,
        AgentModelTier? singleAgentTier = null) =>
        new(templateId, "afonsoft/agent-harness", "/repo/taskboard", "main",
            null, "Implementar endpoint",
            StageOverrides: stageOverrides,
            SingleAgent: singleAgent,
            SingleAgentType: singleAgentType,
            SingleAgentTier: singleAgentTier);

    [Fact]
    public async Task Dado_StageOverrideEmTemplateFixo_Quando_Start_Entao_EstagioUsaAgentePedido()
    {
        var dto = await CriarServico().StartAsync(CriarRequest(stageOverrides:
            new Dictionary<string, PipelineStageOverrideDto>
            {
                ["builder"] = new(AgentType.Codex, AgentModelTier.Ultra),
            }));

        var builder = dto.Stages.Single(s => s.StageKey == "builder");
        builder.Agent.ShouldBe(nameof(AgentType.Codex));
        // Demais estágios mantêm o default do template.
        dto.Stages.Single(s => s.StageKey == "architect").Agent.ShouldBe(nameof(AgentType.Claude));
    }

    [Fact]
    public async Task Dado_StageOverrideEmStageNaoAgente_Quando_Start_Entao_InvalidValue()
    {
        var ex = await Should.ThrowAsync<DomainException>(() => CriarServico().StartAsync(
            CriarRequest(stageOverrides: new Dictionary<string, PipelineStageOverrideDto>
            {
                ["approve-plan"] = new(AgentType.Codex, null),
            })));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_StageOverrideChaveDesconhecida_Quando_Start_Entao_InvalidValue()
    {
        var ex = await Should.ThrowAsync<DomainException>(() => CriarServico().StartAsync(
            CriarRequest(stageOverrides: new Dictionary<string, PipelineStageOverrideDto>
            {
                ["nao-existe"] = new(AgentType.Codex, null),
            })));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_OverrideComCliInelegivel_Quando_Start_Entao_AgentNotEligible()
    {
        Elegiveis(AgentType.Codex, AgentType.OpenCode);

        var ex = await Should.ThrowAsync<DomainException>(() => CriarServico().StartAsync(
            CriarRequest(stageOverrides: new Dictionary<string, PipelineStageOverrideDto>
            {
                ["builder"] = new(AgentType.Claude, null),
            })));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.AgentNotEligible);
    }

    [Fact]
    public async Task Dado_DefaultDoTemplateInelegivel_Quando_Start_Entao_ResolveParaPrimeiroElegivel()
    {
        // architect → Claude no template; Claude inelegível → Auto cai no
        // primeiro elegível em ordem de enum (Devin < Claude < Codex < …).
        Elegiveis(AgentType.Codex, AgentType.OpenCode, AgentType.Devin);

        var dto = await CriarServico().StartAsync(CriarRequest());

        dto.Stages.Single(s => s.StageKey == "architect").Agent.ShouldBe(nameof(AgentType.Devin));
        dto.Stages.Single(s => s.StageKey == "builder").Agent.ShouldBe(nameof(AgentType.OpenCode));
        dto.Stages.Single(s => s.StageKey == "reviewer").Agent.ShouldBe(nameof(AgentType.Devin));
    }

    [Fact]
    public async Task Dado_NenhumCliElegivel_Quando_Start_Entao_AgentNotEligible()
    {
        Elegiveis();

        var ex = await Should.ThrowAsync<DomainException>(() =>
            CriarServico().StartAsync(CriarRequest()));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.AgentNotEligible);
    }

    [Fact]
    public async Task Dado_SingleAgentExplicito_Quando_TemplateMultiEstagio_Entao_TodosAgentWorkUsamMesmoCli()
    {
        var dto = await CriarServico().StartAsync(CriarRequest(
            singleAgent: true, singleAgentType: AgentType.Codex));

        var agentWork = dto.Stages.Where(s => s.Kind == "AgentWork").ToList();
        agentWork.ShouldAllBe(s => s.Agent == nameof(AgentType.Codex));
        // Approval e Verification não recebem CLI.
        dto.Stages.Single(s => s.StageKey == "approve-plan").Agent.ShouldBeNull();
        dto.Stages.Single(s => s.StageKey == "verifier").Agent.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_SingleAgentAuto_Quando_Start_Entao_UmUnicoCliElegivelParaTodasEtapas()
    {
        Elegiveis(AgentType.OpenCode, AgentType.Codex);

        var dto = await CriarServico().StartAsync(CriarRequest(singleAgent: true));

        var agentWork = dto.Stages.Where(s => s.Kind == "AgentWork").ToList();
        agentWork.ShouldAllBe(s => s.Agent == nameof(AgentType.Codex));
    }

    [Fact]
    public async Task Dado_SingleAgentInelegivel_Quando_Start_Entao_AgentNotEligible()
    {
        Elegiveis(AgentType.Codex);

        var ex = await Should.ThrowAsync<DomainException>(() => CriarServico().StartAsync(
            CriarRequest(singleAgent: true, singleAgentType: AgentType.Claude)));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.AgentNotEligible);
    }

    [Fact]
    public async Task Dado_SingleAgentComTier_Quando_Start_Entao_TierAplicadoATodosAgentWork()
    {
        var dto = await CriarServico().StartAsync(CriarRequest(
            singleAgent: true,
            singleAgentType: AgentType.OpenCode,
            singleAgentTier: AgentModelTier.Lite));

        dto.Stages.Where(s => s.Kind == "AgentWork")
            .ShouldAllBe(s => s.Agent == nameof(AgentType.OpenCode));
    }

    [Fact]
    public async Task Dado_ListaTemplates_Quando_Get_Entao_ExpoeDetalhePorEstagio()
    {
        var templates = await CriarServico().ListTemplatesAsync();

        var standard = templates.Single(t => t.TemplateId == "standard-feature");
        standard.Stages.Select(s => s.Key)
            .ShouldBe(["architect", "approve-plan", "builder", "verifier", "reviewer"]);
        var architect = standard.Stages[0];
        architect.Kind.ShouldBe("AgentWork");
        architect.DefaultAgent.ShouldBe(nameof(AgentType.Claude));
        architect.DefaultTier.ShouldBe(nameof(AgentModelTier.Ultra));
    }
}
