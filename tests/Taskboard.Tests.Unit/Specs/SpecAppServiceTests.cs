using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Application.Contracts.Workspace;
using Taskboard.Application.Specs;
using Taskboard.Integrations.Specs;
using Taskboard.Specs;
using Xunit;

namespace Taskboard.Tests.Unit.Specs;

/// <summary>SPEC-20260919-ade-living-specs §5/§8 — file-backed spec catalog.</summary>
public class SpecAppServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _workspaceRoot;

    public SpecAppServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "specs-" + Guid.NewGuid().ToString("N"));
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_workspaceRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        Directory.Delete(_workspaceRoot, recursive: true);
    }

    /// <summary>Minimal stand-in for WorkspaceService — &lt;root&gt;/&lt;repo-name&gt; when cloned.</summary>
    private sealed class FakeWorkspaceResolver(string root) : IWorkspacePathResolver
    {
        public string ResolveCardWorkdir(string? repositoryFullName, out bool exists)
        {
            var name = repositoryFullName?.Split('/')[^1] ?? string.Empty;
            var dir = Path.Combine(root, name);
            exists = !string.IsNullOrEmpty(name) && Directory.Exists(dir);
            return exists ? dir : root;
        }
    }

    private SpecAppService CriarService() =>
        new(
            new MarkdigSpecParser(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Taskboard:SpecsDir"] = _dir
                })
                .Build(),
            new FakeWorkspaceResolver(_workspaceRoot),
            NullLogger<SpecAppService>.Instance);

    private string EscreverSpec(string nome, string status = "`Draft`") =>
        EscreverSpecEm(_dir, nome, status);

    private static string EscreverSpecEm(string dir, string nome, string status)
    {
        var path = Path.Combine(dir, nome + ".md");
        File.WriteAllText(path, $"""
            # {nome}

            ## 0. Metadata

            | Field | Value |
            |---|---|
            | Status | {status} |
            | Feature | `x` |

            ## 4. Requirements

            ### RF-001: algo
            """);
        return path;
    }

    private string ClonarRepoComSpec(string repoName, string specNome)
    {
        var specsDir = Path.Combine(_workspaceRoot, repoName, ".specs");
        Directory.CreateDirectory(specsDir);
        return EscreverSpecEm(specsDir, specNome, "`Draft`");
    }

    [Fact]
    public async Task Dado_DuasSpecs_Quando_List_Entao_RetornaDtos()
    {
        EscreverSpec("SPEC-1-a", "`Draft`");
        EscreverSpec("SPEC-2-b", "`Done`");

        var lista = await CriarService().ListAsync(null, null);

        lista.Count.ShouldBe(2);
        lista.Select(s => s.Id).ShouldBe(["SPEC-2-b", "SPEC-1-a"]);
    }

    [Fact]
    public async Task Dado_FiltroStatus_Quando_List_Entao_SoDoStatus()
    {
        EscreverSpec("SPEC-1-a", "`Draft`");
        EscreverSpec("SPEC-2-b", "`Done`");

        var lista = await CriarService().ListAsync("Done", null);

        lista.Select(s => s.Id).ShouldBe(["SPEC-2-b"]);
    }

    [Fact]
    public async Task Dado_BuscaPorTexto_Quando_List_Entao_FiltraPorIdOuTitulo()
    {
        EscreverSpec("SPEC-1-alpha", "`Draft`");
        EscreverSpec("SPEC-2-beta", "`Draft`");

        var lista = await CriarService().ListAsync(null, "ALPHA");

        lista.Select(s => s.Id).ShouldBe(["SPEC-1-alpha"]);
    }

    [Fact]
    public async Task Dado_SpecExistente_Quando_UpdateStatus_Entao_ReescreveSoACelula()
    {
        var path = EscreverSpec("SPEC-1-a", "`Draft`");
        var original = await File.ReadAllTextAsync(path);

        var dto = await CriarService().UpdateStatusAsync("SPEC-1-a", "Approved");

        dto!.Status.ShouldBe("Approved");
        var novo = await File.ReadAllTextAsync(path);
        novo.ShouldContain("| Status | `Approved` |");
        novo.ShouldContain("### RF-001: algo");
        novo.Length.ShouldBe(original.Length + "Approved".Length - "Draft".Length);
    }

    [Fact]
    public async Task Dado_StatusInvalido_Quando_UpdateStatus_Entao_400()
    {
        EscreverSpec("SPEC-1-a");

        var ex = await Should.ThrowAsync<DomainException>(
            () => CriarService().UpdateStatusAsync("SPEC-1-a", "Inexistente"));

        ex.Code.ShouldBe(TaskboardDomainErrorCodes.InvalidSpecStatus);
    }

    [Fact]
    public async Task Dado_SpecInexistente_Quando_UpdateStatus_Entao_Null()
    {
        var dto = await CriarService().UpdateStatusAsync("SPEC-9-x", "Done");

        dto.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_SpecInexistente_Quando_Get_Entao_Null()
    {
        (await CriarService().GetAsync("SPEC-9-x")).ShouldBeNull();
    }

    [Fact]
    public async Task Dado_DirInexistente_Quando_List_Entao_Vazio()
    {
        var service = new SpecAppService(
            new MarkdigSpecParser(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Taskboard:SpecsDir"] = Path.Combine(_dir, "nao-existe")
                })
                .Build(),
            new FakeWorkspaceResolver(_workspaceRoot),
            NullLogger<SpecAppService>.Instance);

        (await service.ListAsync(null, null)).ShouldBeEmpty();
    }

    // SPEC-20260920-global-repo-selector RF-005 — catalog per repository clone.

    [Fact]
    public async Task Dado_RepoClonadoComSpecs_Quando_ListComRepo_Entao_LeDoClone()
    {
        ClonarRepoComSpec("myrepo", "SPEC-9-repo");
        EscreverSpec("SPEC-1-default"); // default dir must NOT leak into repo results

        var lista = await CriarService().ListAsync(null, null, "owner/myrepo");

        lista.Select(s => s.Id).ShouldBe(["SPEC-9-repo"]);
    }

    [Fact]
    public async Task Dado_RepoSemClone_Quando_ListComRepo_Entao_Vazio()
    {
        var lista = await CriarService().ListAsync(null, null, "owner/fantasma");

        lista.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_RepoClonadoSemSpecsDir_Quando_ListComRepo_Entao_Vazio()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "myrepo"));

        var lista = await CriarService().ListAsync(null, null, "owner/myrepo");

        lista.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_RepoClonadoComSpecs_Quando_GetComRepo_Entao_DetailComRepoPath()
    {
        ClonarRepoComSpec("myrepo", "SPEC-9-repo");

        var dto = await CriarService().GetAsync("SPEC-9-repo", "owner/myrepo");

        dto.ShouldNotBeNull();
        dto.RepositoryPath.ShouldBe(Path.Combine(_workspaceRoot, "myrepo"));
    }

    [Fact]
    public async Task Dado_RepoClonadoComSpecs_Quando_UpdateStatusComRepo_Entao_ReescreveNoClone()
    {
        var path = ClonarRepoComSpec("myrepo", "SPEC-9-repo");

        var dto = await CriarService().UpdateStatusAsync("SPEC-9-repo", "Approved", "owner/myrepo");

        dto!.Status.ShouldBe("Approved");
        (await File.ReadAllTextAsync(path)).ShouldContain("| Status | `Approved` |");
    }

    [Fact]
    public async Task Dado_RepoSemClone_Quando_GetComRepo_Entao_Null()
    {
        (await CriarService().GetAsync("SPEC-9-repo", "owner/fantasma")).ShouldBeNull();
    }
}
