using System.Net;
using System.Net.Http.Json;
using Taskboard.Dtos;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260919-harness-security-permission-gateway §5 — POST /api/harness/security/evaluate.
/// </summary>
public class HarnessSecurityEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public HarnessSecurityEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // AC-01: rm -rf / → Dangerous + bloqueado imediatamente.
    [Fact]
    public async Task Dado_RmRfRaiz_Quando_PostEvaluate_Entao_DangerousNegado()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var worktree = Path.Combine(Path.GetTempPath(), $"tb-eval-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync("/api/harness/security/evaluate",
            new SecurityEvaluateRequestDto("bash", "rm -rf /", worktree, "Standard"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SecurityEvaluationDto>();
        result.ShouldNotBeNull();
        result.RiskLevel.ShouldBe("Dangerous");
        result.Allowed.ShouldBeFalse();
        result.RequiresApproval.ShouldBeFalse();
    }

    // SPEC §5 exemplo: rm -rf bin/ obj/ dentro do worktree → WorkspaceWrite permitido.
    [Fact]
    public async Task Dado_RmDentroDoWorktree_Quando_PostEvaluate_Entao_WorkspaceWritePermitido()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var worktree = Path.Combine(Path.GetTempPath(), $"tb-eval-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync("/api/harness/security/evaluate",
            new SecurityEvaluateRequestDto("bash", "rm -rf bin/ obj/", worktree, "Standard"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SecurityEvaluationDto>();
        result.ShouldNotBeNull();
        result.Allowed.ShouldBeTrue();
        result.RiskLevel.ShouldBe("WorkspaceWrite");
    }

    // AC-02: escrita fora do worktree → 400 SECURITY_ACCESS_DENIED.
    [Fact]
    public async Task Dado_WriteForaDoJail_Quando_PostEvaluate_Entao_AccessDenied()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var worktree = Path.Combine(Path.GetTempPath(), $"tb-eval-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync("/api/harness/security/evaluate",
            new SecurityEvaluateRequestDto("write_file", "/home/ubuntu/.ssh/authorized_keys", worktree, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain(TaskboardDomainErrorCodes.SecurityAccessDenied);
    }

    [Fact]
    public async Task Dado_SudoSobStandard_Quando_PostEvaluate_Entao_RequerAprovacao()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var worktree = Path.Combine(Path.GetTempPath(), $"tb-eval-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync("/api/harness/security/evaluate",
            new SecurityEvaluateRequestDto("bash", "sudo apt install x", worktree, "Standard"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SecurityEvaluationDto>();
        result.ShouldNotBeNull();
        result.Allowed.ShouldBeFalse();
        result.RequiresApproval.ShouldBeTrue();
    }
}
