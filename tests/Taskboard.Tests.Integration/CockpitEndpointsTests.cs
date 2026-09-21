using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Taskboard.Agents;
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
        new(template, "afonsoft/agent-harness", "main", "150", null, "Implementar JWT");

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
    public async Task Dado_OverrideEmTemplateFixo_Quando_PostRuns_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/harness/runs",
            StartRequest() with { AgentOverride = AgentType.Claude });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_SingleAgentComOverride_Quando_PostRuns_Entao_201ComAgenteAplicado()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(
            "/api/harness/runs",
            new RunStartRequest(
                PipelineTemplateIds.SingleAgent, "afonsoft/agent-harness", "main", null, null,
                "Implementar JWT", AgentOverride: AgentType.OpenCode, SkipVerification: true));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();
        dto.ShouldNotBeNull();
        var builder = dto.Stages.Where(s => s.StageKey == "builder").ShouldHaveSingleItem();
        builder.Agent.ShouldBe(nameof(AgentType.OpenCode));
        dto.Stages.ShouldNotContain(s => s.Kind == "Verification");
    }

    [Fact]
    public async Task Dado_RunComIssue_Quando_PostRuns_Entao_HistoryVinculaPipelineRun()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var issueId = $"issue-uni-{Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync(
            "/api/harness/runs",
            StartRequest() with { IssueId = issueId });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var history = await client.GetFromJsonAsync<JsonObject>($"/api/github/issues/{issueId}/history");
        history.ShouldNotBeNull();
        var items = history["items"]!.AsArray();
        var link = items.FirstOrDefault(i => i?["kind"]?.GetValue<string>() == "pipeline-run");
        link.ShouldNotBeNull("a run deve registrar um evento pipeline-run no histórico da issue");
        link!["detail"]!.GetValue<string>().ShouldBe(dto!.PipelineExecutionId);
    }

    [Fact]
    public async Task Dado_RunAtivo_Quando_PauseResume_Entao_200ComStatusCorreto()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var paused = await client.PostAsJsonAsync(
            $"/api/harness/runs/{dto!.PipelineExecutionId}/pause", (object?)null);

        paused.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await paused.Content.ReadFromJsonAsync<PipelineExecutionDto>())!
            .Status.ShouldBe("Paused");

        var resumed = await client.PostAsJsonAsync(
            $"/api/harness/runs/{dto.PipelineExecutionId}/resume", (object?)null);

        resumed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await resumed.Content.ReadFromJsonAsync<PipelineExecutionDto>())!
            .Status.ShouldBe("Running");
    }

    [Fact]
    public async Task Dado_RunPaused_Quando_PauseNovamente_Entao_409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();
        await client.PostAsJsonAsync($"/api/harness/runs/{dto!.PipelineExecutionId}/pause", (object?)null);

        var again = await client.PostAsJsonAsync(
            $"/api/harness/runs/{dto.PipelineExecutionId}/pause", (object?)null);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_RunRunning_Quando_Resume_Entao_409()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest());
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        var response = await client.PostAsJsonAsync(
            $"/api/harness/runs/{dto!.PipelineExecutionId}/resume", (object?)null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dado_RunInexistente_Quando_PauseOuResume_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var pause = await client.PostAsJsonAsync("/api/harness/runs/run_naoexiste/pause", (object?)null);
        var resume = await client.PostAsJsonAsync("/api/harness/runs/run_naoexiste/resume", (object?)null);

        pause.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        resume.StatusCode.ShouldBe(HttpStatusCode.NotFound);
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
