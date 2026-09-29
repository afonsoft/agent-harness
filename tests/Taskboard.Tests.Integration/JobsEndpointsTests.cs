using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Taskboard.Application.Contracts.Jobs;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260929-jobs-dashboard RF-004 — /api/jobs surface: lista, override e
/// run manual. No host de teste os jobs de refresh ficam restritos pelo
/// registro normal (intervalos grandes não disparam no tempo do teste).
/// </summary>
public class JobsEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public JobsEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dado_Lista_Quando_GetJobs_Entao_CatalogoCompleto()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var jobs = await client.GetFromJsonAsync<List<JobStatusDto>>("/api/jobs");

        jobs.ShouldNotBeNull();
        jobs.ShouldContain(j => j.Key == "cli-probe-refresh" && j.IntervalSeconds == 3600);
        jobs.ShouldContain(j => j.Key == "skills-sync" && j.RunOnce);
        jobs.ShouldContain(j => j.Key == "finops-aggregation" && j.IntervalSeconds == 30);
        jobs.ShouldContain(j => j.Key == "stale-run-reaper");
        jobs.ShouldContain(j => j.Key == "spec-drift-scan");
    }

    [Fact]
    public async Task Dado_JobDesconhecido_Quando_Put_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync("/api/jobs/unknown-job", new UpdateJobRequest(true, null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_IntervaloAbaixoDoMin_Quando_Put_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // spec-drift-scan exige intervalo >= 300s.
        var response = await client.PutAsJsonAsync("/api/jobs/spec-drift-scan", new UpdateJobRequest(null, 60));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_OverrideValido_Quando_Put_Entao_AplicadoEPersistido()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync("/api/jobs/spec-drift-scan", new UpdateJobRequest(false, 600));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<JobStatusDto>();
        status.ShouldNotBeNull();
        status.Enabled.ShouldBeFalse();
        status.IntervalSeconds.ShouldBe(600);

        var jobs = await client.GetFromJsonAsync<List<JobStatusDto>>("/api/jobs");
        jobs!.Single(j => j.Key == "spec-drift-scan").Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_JobConhecido_Quando_Run_Entao_202()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/jobs/spec-drift-scan/run", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Dado_JobDesconhecido_Quando_Run_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/jobs/unknown-job/run", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
