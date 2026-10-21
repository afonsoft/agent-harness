using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Taskboard.Application.Contracts.Harness;
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
    private readonly string _tempRoot = Path.Join(Path.GetTempPath(), $"wt-itest-{Guid.NewGuid():N}");

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

    // ---------- SPEC-20261020-worktree-shared-context RF-006/RF-008 ----------

    [Fact]
    public async Task Dado_Worktree_Quando_CardPatchSemStatus_Entao_DerivaClean()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var runId = await CriarWorktreeAsync(client);
        var session = await client.GetFromJsonAsync<WorktreeSessionDto>(
            $"/api/harness/worktrees/{runId}");

        var patch = await client.PatchAsJsonAsync("/api/harness/worktrees/card", new
        {
            worktreePath = session!.Path,
            comment = "exploring spec drift",
        });
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await patch.Content.ReadFromJsonAsync<WorktreeCardDto>();
        card!.WorkspaceStatus.ShouldBe(WorkspaceStatus.Clean);
        card.Comment.ShouldBe("exploring spec drift");
        card.RepoPath.ShouldBe(session.RepositoryPath);

        var fetched = await client.GetFromJsonAsync<WorktreeCardDto>(
            $"/api/harness/worktrees/card?worktreePath={Uri.EscapeDataString(session.Path)}");
        fetched!.Comment.ShouldBe("exploring spec drift");

        var list = await client.GetFromJsonAsync<List<WorktreeCardDto>>(
            $"/api/harness/worktrees/cards?repoPath={Uri.EscapeDataString(session.RepositoryPath)}");
        list!.ShouldContain(c => c.WorktreePath == session.Path);
    }

    [Fact]
    public async Task Dado_Worktree_Quando_CardPatchStatusInvalido_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var patch = await client.PatchAsJsonAsync("/api/harness/worktrees/card", new
        {
            worktreePath = _tempRoot,
            status = "bogus",
        });

        patch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dado_PathNaoGit_Quando_CardPatch_Entao_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        Directory.CreateDirectory(_tempRoot);

        var patch = await client.PatchAsJsonAsync("/api/harness/worktrees/card", new
        {
            worktreePath = _tempRoot,
            comment = "not a repo",
        });

        patch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------- SPEC-20261021-worktree-setup-scripts P1 ----------

    [Fact]
    public async Task Dado_YamlComSetup_Quando_CriaWorktree_Entao_SessionECardOk()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var (session, card) = await SessionECardAsync(client, """
            setup:
              - echo harness-setup-ran
            """);

        session.SetupState.ShouldBe(WorktreeSetupStates.Ok);
        session.SetupLogTail!.ShouldContain("harness-setup-ran");
        card.SetupState.ShouldBe(WorktreeSetupStates.Ok);
        card.SetupFinishedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Dado_SetupFalhando_Quando_CriaWorktree_Entao_CardFailedComExit()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var (session, card) = await SessionECardAsync(client, "setup: [exit 42]");

        session.SetupState.ShouldBe(WorktreeSetupStates.Failed);
        card.SetupState.ShouldBe(WorktreeSetupStates.Failed);
        card.SetupExitCode.ShouldBe(42);
    }

    [Fact]
    public async Task Dado_SemSetup_Quando_CriaWorktree_Entao_SessionSemSetup()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var runId = await CriarWorktreeAsync(client);

        var session = await client.GetFromJsonAsync<WorktreeSessionDto>(
            $"/api/harness/worktrees/{runId}");

        session!.SetupState.ShouldBeNull();
    }

    private async Task<(WorktreeSessionDto Session, WorktreeCardDto Card)>
        SessionECardAsync(HttpClient client, string yaml)
    {
        var runId = await CriarWorktreeAsync(client, yaml);
        var session = (await client.GetFromJsonAsync<WorktreeSessionDto>(
            $"/api/harness/worktrees/{runId}"))!;
        var card = (await client.GetFromJsonAsync<WorktreeCardDto>(
            $"/api/harness/worktrees/card?worktreePath={Uri.EscapeDataString(session.Path)}"))!;
        return (session, card);
    }

    private async Task<string> CriarWorktreeAsync(HttpClient client, string? yaml = null)
    {
        var repoPath = Path.Join(_tempRoot, "repo");
        Directory.CreateDirectory(repoPath);
        var git = new GitCommandRunner();
        (await git.RunAsync(repoPath, ["init", "-b", "main"])).ExitCode.ShouldBe(0);
        await git.RunAsync(repoPath, ["config", "user.email", "itest@local"]);
        await git.RunAsync(repoPath, ["config", "user.name", "itest"]);
        await File.WriteAllTextAsync(Path.Join(repoPath, "README.md"), "# repo\n");
        if (yaml is not null)
        {
            Directory.CreateDirectory(Path.Join(repoPath, ".harness"));
            await File.WriteAllTextAsync(
                Path.Join(repoPath, ".harness", "worktree.yaml"), yaml);
        }

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
