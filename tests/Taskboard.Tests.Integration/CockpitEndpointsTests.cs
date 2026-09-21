using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260919-ade-cockpit-hitl §5 — /api/harness/runs + /harness-cockpit-hub.
/// O pipeline fica pre-dispatch no host de teste (worktree falha em HomeDir
/// vazio), então os testes cobrem a superfície de API e o stream de eventos.
/// </summary>
public class CockpitEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public CockpitEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static RunStartRequest StartRequest(string template = "quick-patch") =>
        new(template, "afonsoft/taskboard-ai", "main", "150", null, "Implementar JWT");

    [Fact]
    public async Task Dado_RunValido_Quando_PostRuns_Entao_201EApareceNaLista()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest());

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();
        dto.ShouldNotBeNull();
        dto.TemplateId.ShouldBe("quick-patch");

        var list = await client.GetFromJsonAsync<List<PipelineExecutionDto>>("/api/harness/runs");
        list.ShouldNotBeNull();
        list.ShouldContain(r => r.PipelineExecutionId == dto.PipelineExecutionId);
    }

    [Fact]
    public async Task Dado_RunComSpec_Quando_PostRuns_Entao_201()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(
            "/api/harness/runs",
            StartRequest() with { SpecPath = ".specs/SPEC-20260919-ade-cockpit-hitl.md" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Dado_RunInexistente_Quando_Get_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/harness/runs/run_naoexiste");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_RunExistente_Quando_Get_Entao_200ComExecucaoTelemetriaWorktree()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var details = await client.GetFromJsonAsync<RunDetailsDto>($"/api/harness/runs/{dto!.PipelineExecutionId}");

        details.ShouldNotBeNull();
        details.Execution.PipelineExecutionId.ShouldBe(dto.PipelineExecutionId);
        details.Execution.Stages.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Dado_Steer_Quando_Post_Entao_202EEventoNoBuffer()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var steer = await client.PostAsJsonAsync(
            $"/api/harness/runs/{dto!.PipelineExecutionId}/steer",
            new SteerRequest("priorize o refresh token"));

        steer.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var events = await client.GetFromJsonAsync<List<CockpitEventDto>>(
            $"/api/harness/runs/{dto.PipelineExecutionId}/events");
        events.ShouldNotBeNull();
        events.ShouldContain(e => e.Kind == "steer" && e.PayloadJson == "priorize o refresh token");
    }

    [Fact]
    public async Task Dado_SteerVazio_Quando_Post_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var steer = await client.PostAsJsonAsync(
            $"/api/harness/runs/{dto!.PipelineExecutionId}/steer",
            new SteerRequest("   "));

        steer.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_RequestIdSemPrefixoStage_Quando_PostApproval_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var response = await client.PostAsJsonAsync(
            $"/api/harness/runs/{dto!.PipelineExecutionId}/approvals/req-123",
            new ApprovalReplyRequest("Allow", null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_GateStage_Quando_DenyApproval_Entao_StageFailed()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var response = await client.PostAsJsonAsync(
            $"/api/harness/runs/{dto!.PipelineExecutionId}/approvals/stage:builder",
            new ApprovalReplyRequest("Deny", "plano incompleto"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var execution = await response.Content.ReadFromJsonAsync<PipelineExecutionDto>();
        var builder = execution!.Stages.Single(s => s.StageKey == "builder");
        builder.Status.ShouldBe("Failed");
        builder.LastError.ShouldNotBeNull().ShouldContain("plano incompleto");
    }

    [Fact]
    public async Task Dado_RunNaoCompletado_Quando_CreatePr_Entao_409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var response = await client.PostAsJsonAsync(
            $"/api/harness/runs/{dto!.PipelineExecutionId}/create-pr",
            new CreatePrRequest("feat: jwt", null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_RunInexistente_Quando_CreatePr_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/harness/runs/run_naoexiste/create-pr",
            new CreatePrRequest("feat: jwt", null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_ClienteNoGrupo_Quando_SteerPublicado_Entao_RecebeEventoNoHub()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        await using var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/harness-cockpit-hub", options =>
            {
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers["X-Api-Key"] = TaskboardWebApplicationFactory.TestApiKey;
            })
            .Build();

        var received = new TaskCompletionSource<CockpitEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<CockpitEventDto>("ReceiveCockpitEvent", evt =>
        {
            if (evt.Kind == "steer")
            {
                received.TrySetResult(evt);
            }
        });
        await connection.StartAsync();
        await connection.InvokeAsync("JoinRunGroup", dto!.PipelineExecutionId);

        await client.PostAsJsonAsync(
            $"/api/harness/runs/{dto.PipelineExecutionId}/steer",
            new SteerRequest("corrija o middleware"));

        var done = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        done.ShouldBe(received.Task, "o evento steer deve chegar ao grupo do run via SignalR");
        var evt = await received.Task;
        evt.PayloadJson.ShouldBe("corrija o middleware");
        await connection.StopAsync();
    }
}
