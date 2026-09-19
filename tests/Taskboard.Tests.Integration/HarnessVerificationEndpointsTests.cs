using System.Net;
using System.Net.Http.Json;
using Taskboard.Dtos;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260919-harness-verification-loop §5 — POST /api/harness/verification/run.
/// </summary>
public class HarnessVerificationEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public HarnessVerificationEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // Build de solução inexistente falha rápido → BuildFailed + evidência persistida.
    [Fact]
    public async Task Dado_SolutionInexistente_Quando_PostRun_Entao_BuildFailed()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var worktree = Path.Combine(Path.GetTempPath(), $"tb-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(worktree);

        var response = await client.PostAsJsonAsync("/api/harness/verification/run",
            new VerificationRunRequestDto(worktree, "Missing.sln", 0.0));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<VerificationReportDto>();
        report.ShouldNotBeNull();
        report.IsSuccess.ShouldBeFalse();
        report.Status.ShouldBe("BuildFailed");
        report.FeedbackPrompt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_PostRun_Entao_NaoAutorizado()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/harness/verification/run",
            new VerificationRunRequestDto("/tmp/x", "x.sln", 0.0));

        ((int)response.StatusCode).ShouldBeOneOf(401, 302);
    }
}
