using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260919-ade-multi-agent-orchestration §5 — /api/harness/pipelines.
/// The factory's HomeDir is empty, so worktree creation fails gracefully and
/// executions stay pre-dispatch — enough to cover the endpoint surface.
/// </summary>
public class PipelineEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public PipelineEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static PipelineStartRequest StartRequest(string template = "quick-patch") =>
        new(template, "afonsoft/agent-harness", "/repo/taskboard", "main", "150", "Implementar JWT");

    [Fact]
    public async Task Dado_Autenticado_Quando_GetTemplates_Entao_ListaOs4Templates()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/harness/pipelines/templates");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var templates = await response.Content.ReadFromJsonAsync<List<PipelineTemplateDto>>();
        templates.ShouldNotBeNull();
        templates.Select(t => t.TemplateId).ShouldBe(
            ["standard-feature", "quick-patch", "test-driven", "single-agent"]);
    }

    [Fact]
    public async Task Dado_Templates_Quando_Get_Entao_ExpoeStagesComDefaults()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var templates = await client.GetFromJsonAsync<List<PipelineTemplateDto>>("/api/harness/pipelines/templates");

        var standard = templates!.Single(t => t.TemplateId == "standard-feature");
        standard.Stages.Select(s => s.Key)
            .ShouldBe(["architect", "approve-plan", "builder", "verifier", "reviewer"]);
        standard.Stages[0].Kind.ShouldBe("AgentWork");
        standard.Stages[0].DefaultAgent.ShouldBe("Claude");
        standard.Stages[1].Kind.ShouldBe("Approval");
    }

    [Fact]
    public async Task Dado_StageOverrideEmTemplateFixo_Quando_Start_Entao_201ComAgentePedido()
    {
        // SPEC-20260922 RF-001: overrides por stage valem para qualquer template.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync("/api/harness/pipelines/start",
            StartRequest() with
            {
                StageOverrides = new Dictionary<string, PipelineStageOverrideDto>
                {
                    ["builder"] = new(Taskboard.Agents.AgentType.Devin, null),
                },
            });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();
        dto!.Stages.Single(s => s.StageKey == "builder").Agent.ShouldBe("Devin");
    }

    [Fact]
    public async Task Dado_StageOverrideEmEstagioNaoAgente_Quando_Start_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync("/api/harness/pipelines/start",
            StartRequest("standard-feature") with
            {
                StageOverrides = new Dictionary<string, PipelineStageOverrideDto>
                {
                    ["approve-plan"] = new(Taskboard.Agents.AgentType.Codex, null),
                },
            });

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_SingleAgentEmTemplateMultiEstagio_Quando_Start_Entao_TodosAgentWorkComMesmoCli()
    {
        // SPEC-20260922 RF-005: um único CLI para todo o fluxo, preservando
        // Approval/Verification sem agente.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync("/api/harness/pipelines/start",
            StartRequest("standard-feature") with
            {
                SingleAgent = true,
                SingleAgentType = Taskboard.Agents.AgentType.Codex,
            });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();
        dto!.Stages.Where(s => s.Kind == "AgentWork")
            .ShouldAllBe(s => s.Agent == "Codex");
        dto.Stages.Single(s => s.StageKey == "approve-plan").Agent.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_TemplateInvalido_Quando_PostStart_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/harness/pipelines/start", StartRequest("nao-existe"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_PipelineValido_Quando_StartEGet_Entao_201ComEstagios()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync("/api/harness/pipelines/start", StartRequest());

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();
        dto.ShouldNotBeNull();
        dto.TemplateId.ShouldBe("quick-patch");
        dto.Stages.Select(s => s.StageKey).ShouldBe(["builder", "verifier"]);

        var fetched = await client.GetAsync($"/api/harness/pipelines/{dto.PipelineExecutionId}");
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dado_PipelineInexistente_Quando_Get_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/harness/pipelines/pipe_naoexiste");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_EstagioSemAprovacaoPendente_Quando_Approve_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/pipelines/start", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var response = await client.PostAsJsonAsync(
            $"/api/harness/pipelines/{dto!.PipelineExecutionId}/stages/builder/approve",
            new PipelineApproveRequest("ok"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_EstagioNaoFalho_Quando_Retry_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/pipelines/start", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var response = await client.PostAsJsonAsync(
            $"/api/harness/pipelines/{dto!.PipelineExecutionId}/stages/builder/retry",
            new PipelineRetryRequest("tenta de novo"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_PipelineAtivo_Quando_Cancel_Entao_StatusCancelled()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/pipelines/start", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var response = await client.PostAsync(
            $"/api/harness/pipelines/{dto!.PipelineExecutionId}/cancel", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancelled = await response.Content.ReadFromJsonAsync<PipelineExecutionDto>();
        cancelled!.Status.ShouldBe("Cancelled");
    }

    [Fact]
    public async Task Dado_TriedAgentsVazioLegado_Quando_Get_Entao_200SemJsonException()
    {
        // Regressão: AddPipelineStageTriedAgents criou a coluna com DEFAULT '',
        // que quebrava o JsonSerializer ao materializar stages (HTTP 500).
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/pipelines/start", StartRequest());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<Taskboard.EntityFrameworkCore.Data.TaskboardDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"PipelineStageExecutions\" SET \"TriedAgents\" = '' WHERE \"ExecutionId\" = {0}",
                dto!.PipelineExecutionId);
        }

        var fetched = await client.GetAsync($"/api/harness/pipelines/{dto!.PipelineExecutionId}");

        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetchedDto = await fetched.Content.ReadFromJsonAsync<PipelineExecutionDto>();
        fetchedDto!.Stages.ShouldAllBe(s => s.TriedAgents == null || s.TriedAgents.Count == 0);
    }
}
