using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using Taskboard.Dtos;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260919-ade-living-specs §5 — /api/specs over a temp .specs fixture
/// (real dir must never be written by tests).
/// </summary>
public class SpecEndpointsTests : IClassFixture<SpecEndpointsTests.SpecsFactory>
{
    public sealed class SpecsFactory : TaskboardWebApplicationFactory
    {
        public string SpecsDir { get; } =
            Path.Combine(Path.GetTempPath(), "tb-itest-specs-" + Guid.NewGuid().ToString("N"));

        public SpecsFactory()
        {
            WorkspaceRoot = Path.Combine(Path.GetTempPath(), "tb-itest-ws-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(SpecsDir);
            // SPEC-20260920 RF-005 fixture: clone myrepo with its own .specs/.
            var repoSpecsDir = Path.Combine(WorkspaceRoot, "myrepo", ".specs");
            Directory.CreateDirectory(repoSpecsDir);
            File.WriteAllText(Path.Combine(repoSpecsDir, "SPEC-9-repo.md"), """
                # SPEC-9-repo

                ## 0. Metadata

                | Field | Value |
                |---|---|
                | Status | `Draft` |
                """);
            File.WriteAllText(Path.Combine(SpecsDir, "SPEC-1-alpha.md"), """
                # SPEC-1-alpha

                ## 0. Metadata

                | Field | Value |
                |---|---|
                | Status | `Draft` |

                ## 4. Requirements

                ### RF-001: requisito um

                ## 6. Acceptance Criteria

                - [ ] **Given** x, **when** y, **then** z.
                """);
            File.WriteAllText(Path.Combine(SpecsDir, "SPEC-2-beta.md"), """
                # SPEC-2-beta

                ## 0. Metadata

                | Field | Value |
                |---|---|
                | Status | `Done` |
                """);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Taskboard:SpecsDir", SpecsDir);
        }
    }

    private readonly SpecsFactory _factory;

    public SpecEndpointsTests(SpecsFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetSpecs_Entao_ListaCatalogo()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/specs");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var specs = await response.Content.ReadFromJsonAsync<List<LivingSpecDto>>();
        specs.ShouldNotBeNull();
        specs.Select(s => s.Id).ShouldBe(["SPEC-2-beta", "SPEC-1-alpha"]);
        // Status is mutable via POST .../status — assert only immutable shape here.
        var alpha = specs.Single(s => s.Id == "SPEC-1-alpha");
        alpha.RequirementsCount.ShouldBe(1);
        alpha.AcceptanceCriteriaCount.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_FiltroStatus_Quando_GetSpecs_Entao_SoDoStatus()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var specs = await client.GetFromJsonAsync<List<LivingSpecDto>>("/api/specs?status=Done");

        specs.ShouldNotBeNull();
        specs.Select(s => s.Id).ShouldBe(["SPEC-2-beta"]);
    }

    [Fact]
    public async Task Dado_SpecExistente_Quando_GetPorId_Entao_200ComMarkdown()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/specs/SPEC-1-alpha");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<LivingSpecDetailDto>();
        dto!.Markdown.ShouldContain("RF-001");
        dto.Requirements.Single().Code.ShouldBe("RF-001");
    }

    [Fact]
    public async Task Dado_SpecInexistente_Quando_GetPorId_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/specs/SPEC-9-x");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_SpecDraft_Quando_PostStatus_Entao_AtualizaArquivo()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/specs/SPEC-1-alpha/status", new SpecStatusUpdateRequest("Approved"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<LivingSpecDetailDto>();
        dto!.Status.ShouldBe("Approved");
        (await File.ReadAllTextAsync(Path.Combine(_factory.SpecsDir, "SPEC-1-alpha.md")))
            .ShouldContain("| Status | `Approved` |");
    }

    [Fact]
    public async Task Dado_StatusInvalido_Quando_PostStatus_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/specs/SPEC-2-beta/status", new SpecStatusUpdateRequest("NaoExiste"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_DriftReport_Entao_200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/specs/drift-report");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<SpecDriftReportDto>();
        report!.TotalSpecs.ShouldBe(2);
    }

    // SPEC-20260920-global-repo-selector RF-005 — ?repo=owner/name.

    [Fact]
    public async Task Dado_RepoClonado_Quando_GetSpecsComRepo_Entao_LeDoClone()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var specs = await client.GetFromJsonAsync<List<LivingSpecDto>>("/api/specs?repo=owner/myrepo");

        specs.ShouldNotBeNull();
        specs.Select(s => s.Id).ShouldBe(["SPEC-9-repo"]);
    }

    [Fact]
    public async Task Dado_RepoNaoClonado_Quando_GetSpecsComRepo_Entao_200Vazio()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var specs = await client.GetFromJsonAsync<List<LivingSpecDto>>("/api/specs?repo=owner/fantasma");

        specs.ShouldNotBeNull();
        specs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_RepoMalformado_Quando_GetSpecsComRepo_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/specs?repo=sem-dono");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_RepoClonado_Quando_GetSpecPorIdComRepo_Entao_200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/specs/SPEC-9-repo?repo=owner/myrepo");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dado_RepoMalformado_Quando_GetSpecPorIdComRepo_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/specs/SPEC-9-repo?repo=sem-dono");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_RepoMalformado_Quando_PostStatusComRepo_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/specs/SPEC-9-repo/status?repo=sem-dono", new SpecStatusUpdateRequest("Approved"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_RepoClonado_Quando_DriftReportComRepo_Entao_200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/specs/drift-report?repo=owner/myrepo");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<SpecDriftReportDto>();
        report!.TotalSpecs.ShouldBe(1);
    }
}
