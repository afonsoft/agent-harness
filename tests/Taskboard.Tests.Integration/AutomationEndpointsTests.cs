using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20261021-agent-automations RF-006: /api/automations CRUD + manual
/// run + history — auth required, schedule validation, worktree target path.
/// </summary>
public class AutomationEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AutomationEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetAutomations_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/automations");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_CreateCron_Entao_PersisteERetorna201()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/automations", new
        {
            name = "morning-triage",
            prompt = "Summarize open issues",
            agentCli = "claude",
            target = "repo:demo",
            schedule = "weekdays",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<JsonObject>();
        dto.ShouldNotBeNull();
        dto!["scheduleKind"]!.ToString().ShouldBe("cron");
        dto["cronExpression"]!.ToString().ShouldBe("0 9 * * 1-5");
        dto["enabled"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_CronInvalido_Quando_Create_Entao_Retorna400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/automations", new
        {
            name = "bad",
            prompt = "x",
            agentCli = "claude",
            target = "repo:demo",
            schedule = "not a cron",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_ExtraArgsProibidos_Quando_Create_Entao_Retorna400InvalidExtraArgs()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/automations", new
        {
            name = "bad-args",
            prompt = "x",
            agentCli = "claude",
            target = "repo:demo",
            schedule = "daily",
            extraAgentArgs = "--permission-mode dangerous",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body!["error"]!.ToString().ShouldBe("invalid_extra_args");
    }

    [Fact]
    public async Task Dado_ExtraArgsValidos_Quando_Create_Entao_Persiste()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/automations", new
        {
            name = "with-model",
            prompt = "x",
            agentCli = "claude",
            target = "repo:demo",
            schedule = "daily",
            extraAgentArgs = "--model opus --add-dir /tmp/x",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        body!["extraAgentArgs"]!.ToString().ShouldBe("--model opus --add-dir /tmp/x");
        body["reuseSession"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_Automation_Quando_RunWorktreeInexistente_Entao_RunSkipped()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/automations", new
        {
            name = "manual",
            prompt = "ping",
            agentCli = "claude",
            target = "worktree:/nonexistent-path-xyz",
            schedule = "hourly",
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.ToString();

        var run = await client.PostAsync($"/api/automations/{id}/run", null);

        run.StatusCode.ShouldBe(HttpStatusCode.OK);
        var runDto = await run.Content.ReadFromJsonAsync<JsonObject>();
        runDto.ShouldNotBeNull();
        runDto["outcome"]!.ToString().ShouldBe("skipped");
        runDto["skipReason"]!.ToString().ShouldContain("invalid_target");
    }

    [Fact]
    public async Task Dado_Automation_Quando_Delete_Entao_404EmGet()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/automations", new
        {
            name = "ephemeral",
            prompt = "x",
            agentCli = "claude",
            target = "repo:demo",
            schedule = "daily",
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.ToString();

        (await client.DeleteAsync($"/api/automations/{id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/automations/{id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_Automation_Quando_ListRuns_Entao_IncluiRuns()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/automations", new
        {
            name = "history",
            prompt = "x",
            agentCli = "claude",
            target = "worktree:/nope",
            schedule = "hourly",
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.ToString();
        await client.PostAsync($"/api/automations/{id}/run", null);

        var runs = await client.GetFromJsonAsync<RunsEnvelope>($"/api/automations/{id}/runs");

        runs.ShouldNotBeNull();
        runs.Runs.Count.ShouldBeGreaterThan(0);
        runs.Runs[0].Outcome.ShouldBe("skipped");
    }

    private sealed class RunsEnvelope
    {
        public List<RunRow> Runs { get; set; } = [];
    }

    private sealed class RunRow
    {
        public string Outcome { get; set; } = "";
    }
}
