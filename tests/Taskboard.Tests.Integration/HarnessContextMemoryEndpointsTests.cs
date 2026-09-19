using System.Net;
using System.Net.Http.Json;
using Taskboard.Agents;
using Taskboard.Dtos;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260919-harness-context-memory §5 — POST /api/harness/context/compile
/// e POST/GET/DELETE /api/harness/memory.
/// </summary>
public class HarnessContextMemoryEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly TaskboardWebApplicationFactory _factory;

    public HarnessContextMemoryEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dado_WorktreeComInstructions_Quando_PostCompile_Entao_RetornaSystemPrompt()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var dir = Path.Combine(Path.GetTempPath(), $"tb-compile-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "AGENTS.md"), "# Rules\nSempre usar TDD.");

        var response = await client.PostAsJsonAsync("/api/harness/context/compile",
            new CompileContextRequestDto(dir, AgentType.Codex, 8000));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ContextCompilationDto>();
        result.ShouldNotBeNull();
        result.SystemPrompt.ShouldContain("Sempre usar TDD");
        result.EstimatedTokens.ShouldBeGreaterThan(0);
        result.InjectedFiles.ShouldContain(f => f.EndsWith("AGENTS.md"));
    }

    [Fact]
    public async Task Dado_MemoriaValida_Quando_PostEGetMemory_Entao_PersisteELista()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var repo = $"owner/repo-{Guid.NewGuid():N}";

        var post = await client.PostAsJsonAsync("/api/harness/memory",
            new AddMemoryRequestDto(repo, "decisão de cache",
                "Decidimos usar cache em memória para o catálogo de skills.",
                ["cache", "skills"], "ArchitecturalDecision"));
        post.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await post.Content.ReadFromJsonAsync<ProjectMemoryItemDto>();
        created.ShouldNotBeNull();
        created.Type.ShouldBe("ArchitecturalDecision");
        created.Tags.ShouldContain("cache");

        var list = await client.GetFromJsonAsync<List<ProjectMemoryItemDto>>(
            $"/api/harness/memory?repositoryFullName={repo}");
        list.ShouldNotBeNull();
        list.ShouldContain(m => m.Id == created.Id);

        var search = await client.GetFromJsonAsync<List<ProjectMemoryItemDto>>(
            $"/api/harness/memory?repositoryFullName={repo}&query=cache");
        search.ShouldNotBeNull();
        search.ShouldContain(m => m.Id == created.Id);
    }

    [Fact]
    public async Task Dado_MemoriaPersistida_Quando_DeleteMemory_Entao_RemoveDaLista()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var repo = $"owner/repo-{Guid.NewGuid():N}";

        var post = await client.PostAsJsonAsync("/api/harness/memory",
            new AddMemoryRequestDto(repo, "lição", "conteúdo", null, null));
        post.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await post.Content.ReadFromJsonAsync<ProjectMemoryItemDto>();

        var del = await client.DeleteAsync($"/api/harness/memory/{created!.Id}");
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await client.GetFromJsonAsync<List<ProjectMemoryItemDto>>(
            $"/api/harness/memory?repositoryFullName={repo}");
        list.ShouldNotBeNull();
        list.ShouldNotContain(m => m.Id == created.Id);
    }

    [Fact]
    public async Task Dado_SemAutenticacao_Quando_PostMemory_Entao_Retorna401()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/harness/memory",
            new AddMemoryRequestDto("o/r", "t", "c", null, null));

        // Sem cookie nem X-Api-Key o endpoint redireciona para login ou 401.
        ((int)response.StatusCode).ShouldBeOneOf(401, 302);
    }
}
