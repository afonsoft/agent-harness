using System.Net;
using System.Net.Http.Json;
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
}
