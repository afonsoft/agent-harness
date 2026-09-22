using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Taskboard.Dtos;
using Taskboard.Harness;
using Taskboard.Integrations.Harness;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260921-cockpit-live-logs-explorer-diff RF-003: explorer endpoints —
/// listagem lazy + leitura confinada ao worktree (worktree real via git).
/// </summary>
public class WorktreeEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>, IDisposable
{
    private readonly TaskboardWebApplicationFactory _factory;
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"wt-itest-{Guid.NewGuid():N}");

    public WorktreeEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dado_RunSemWorktree_Quando_Files_Entao_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var list = await client.GetAsync("/api/harness/worktrees/run_sem_wt/files");
        var content = await client.GetAsync("/api/harness/worktrees/run_sem_wt/files/content?path=x");

        list.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        content.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dado_Worktree_Quando_FilesTraversal_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var runId = await CriarWorktreeAsync(client);

        var response = await client.GetAsync(
            $"/api/harness/worktrees/{runId}/files?path=../escape");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_Worktree_Quando_FilesEContent_Entao_ListaELe()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var runId = await CriarWorktreeAsync(client);

        var list = await client.GetFromJsonAsync<WorktreeListDto>(
            $"/api/harness/worktrees/{runId}/files");
        list.ShouldNotBeNull();
        list.Entries.ShouldContain(e => e.Name == "README.md" && !e.Directory);
        list.Entries.ShouldNotContain(e => e.Name == ".git");

        var file = await client.GetFromJsonAsync<WorktreeFileContentDto>(
            $"/api/harness/worktrees/{runId}/files/content?path=README.md");
        file.ShouldNotBeNull();
        file.Content!.ShouldContain("# repo");
        file.Binary.ShouldBeFalse();

        var missing = await client.GetAsync(
            $"/api/harness/worktrees/{runId}/files/content?path=fantasma.cs");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<string> CriarWorktreeAsync(HttpClient client)
    {
        var repoPath = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(repoPath);
        var git = new GitCommandRunner();
        (await git.RunAsync(repoPath, ["init", "-b", "main"])).ExitCode.ShouldBe(0);
        await git.RunAsync(repoPath, ["config", "user.email", "itest@local"]);
        await git.RunAsync(repoPath, ["config", "user.name", "itest"]);
        await File.WriteAllTextAsync(Path.Combine(repoPath, "README.md"), "# repo\n");
        await git.RunAsync(repoPath, ["add", "-A"]);
        (await git.RunAsync(repoPath, ["commit", "-m", "init"])).ExitCode.ShouldBe(0);

        var runId = $"run_{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/harness/worktrees", new
        {
            runId,
            repositoryPath = repoPath,
            baseBranch = "main",
            taskSlug = "explorer-itest",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return runId;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
