using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260923-cockpit-run-hardening RF-001/RF-004 — run start provisions
/// the clone server-side under the workspace root and the events endpoint
/// serves the durable normalized stream.
/// </summary>
public class RunProvisioningEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public RunProvisioningEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static RunStartRequest StartRequest(string repo) =>
        new("single-agent", repo, "main", "150", SpecPath: null, Prompt: "Implementar JWT");

    [Fact]
    public async Task Dado_Start_Quando_Provisioning_Entao_RepositoryPathEhCloneNoServidor()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest("afonsoft/repo-provisioned"));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();
        // O path do cliente não é enviado — o clone canônico vive sob o workspace root.
        dto!.RepositoryPath.ShouldBe(Path.Join(_factory.WorkspaceRoot, "repo-provisioned"));
        Directory.Exists(dto.RepositoryPath).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_DirEstrangeiro_Quando_Start_Entao_422SemReutilizar()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var foreign = Path.Join(_factory.WorkspaceRoot, "repo-estrangeiro");
        Directory.CreateDirectory(foreign);
        await File.WriteAllTextAsync(Path.Join(foreign, ".foreign-repo"), "x");

        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest("afonsoft/repo-estrangeiro"));

        created.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Dado_RunCriado_Quando_GetEvents_Entao_EnvelopeComEventosDuraveis()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/harness/runs", StartRequest("afonsoft/repo-eventos"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<PipelineExecutionDto>();

        // O sink persiste de forma assíncrona (fire-and-forget) — poll até o
        // evento de lifecycle do provisioning aparecer.
        CockpitEventsPage? page = null;
        for (var i = 0; i < 20; i++)
        {
            page = await client.GetFromJsonAsync<CockpitEventsPage>(
                $"/api/harness/runs/{dto!.PipelineExecutionId}/events");
            if (page!.Events.Any(e => e.Kind == "lifecycle" && e.Title.Contains("clone")))
            {
                break;
            }

            await Task.Delay(250);
        }

        page.ShouldNotBeNull();
        page.Events.ShouldContain(e => e.Kind == "lifecycle" && e.Title.Contains("clone"));
    }

    [Fact]
    public async Task Dado_RunInexistente_Quando_GetEvents_Entao_200ComListaVazia()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/harness/runs/pipe_inexistente/events");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<CockpitEventsPage>();
        page!.Events.ShouldBeEmpty();
        page.HasMore.ShouldBeFalse();
    }
}
