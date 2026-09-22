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
/// SPEC-20260920-board-cockpit-unified-runs R1/R2 — o template `single-agent`
/// aceita override de agente/tier e drop do estágio de verificação; overrides
/// em templates fixos são rejeitados com InvalidValue (HTTP 400).
/// </summary>
public class SingleAgentPipelineTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<TaskboardDbContext> _options;
    private readonly TaskboardDbContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentAcpClient _acp = Substitute.For<IAgentAcpClient>();
    private readonly IWorkspaceIsolationService _isolation = Substitute.For<IWorkspaceIsolationService>();
    private readonly IVerificationEngine _verification = Substitute.For<IVerificationEngine>();

    public SingleAgentPipelineTests()
    {
        _dbPath = Path.Join(Path.GetTempPath(), $"tb-single-{Guid.NewGuid()}.sqlite");
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

    private PipelineExecutionAppService CriarServico() =>
        new(
            new EfCoreRepository<PipelineExecution>(_context),
            new PipelineEngine(_scopeFactory, _acp, _verification, NullLogger<PipelineEngine>.Instance),
            Substitute.For<IWorkspaceIsolationService>(),
            Substitute.For<Taskboard.GitHub.IGitHubService>(),
            NullLogger<PipelineExecutionAppService>.Instance);

    private static PipelineStartRequest CriarRequest(
        string templateId = PipelineTemplates.SingleAgentId,
        AgentType? agent = null,
        AgentModelTier? tier = null,
        bool skipVerification = false) =>
        new(templateId, "afonsoft/agent-harness", "/repo/taskboard", "main",
            null, "Implementar endpoint", AgentOverride: agent,
            TierOverride: tier, SkipVerification: skipVerification);

    [Fact]
    public async Task Dado_OverrideAgente_Quando_SingleAgent_Entao_EstagioUsaAgenteETierDoRequest()
    {
        var servico = CriarServico();

        var dto = await servico.StartAsync(
            CriarRequest(agent: AgentType.Claude, tier: AgentModelTier.Ultra));

        var builder = dto.Stages.Where(s => s.StageKey == "builder").ShouldHaveSingleItem();
        builder.Agent.ShouldBe(nameof(AgentType.Claude));
        dto.Stages.ShouldContain(s => s.StageKey == "verifier");
    }

    [Fact]
    public async Task Dado_SkipVerification_Quando_SingleAgent_Entao_SomenteBuilder()
    {
        var servico = CriarServico();

        var dto = await servico.StartAsync(CriarRequest(skipVerification: true));

        dto.Stages.ShouldHaveSingleItem().StageKey.ShouldBe("builder");
    }

    [Fact]
    public async Task Dado_SemOverride_Quando_SingleAgent_Entao_DefaultCodexNormalComVerifier()
    {
        var servico = CriarServico();

        var dto = await servico.StartAsync(CriarRequest());

        var builder = dto.Stages.Where(s => s.StageKey == "builder").ShouldHaveSingleItem();
        builder.Agent.ShouldBe(nameof(AgentType.Codex));
        dto.Stages.ShouldContain(s => s.StageKey == "verifier");
    }

    [Fact]
    public async Task Dado_OverrideEmTemplateFixo_Quando_Start_Entao_InvalidValue()
    {
        var servico = CriarServico();

        var ex = await Should.ThrowAsync<DomainException>(() =>
            servico.StartAsync(CriarRequest(
                templateId: "quick-patch", agent: AgentType.Devin)));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task Dado_SkipEmTemplateFixo_Quando_Start_Entao_InvalidValue()
    {
        var servico = CriarServico();

        var ex = await Should.ThrowAsync<DomainException>(() =>
            servico.StartAsync(CriarRequest(templateId: "standard-feature", skipVerification: true)));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidValue);
    }

    [Fact]
    public void Dado_Templates_Quando_Lista_Entao_ContemSingleAgent()
    {
        PipelineTemplates.All.ShouldContain(t =>
            t.TemplateId == PipelineTemplates.SingleAgentId
            && t.Stages.Count == 2
            && t.Stages[0].Key == "builder"
            && t.Stages[1].Key == "verifier");
    }
}
