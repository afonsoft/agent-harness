using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Integrations.Workspace;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Workspace;

public class WorkspaceServiceTests : IDisposable
{
    private readonly string _home = Path.Join(Path.GetTempPath(), "tb-ws-" + Guid.NewGuid().ToString("N"));

    private WorkspaceService Create(string? configured = null) =>
        new(configured, _home, NullLogger<WorkspaceService>.Instance);

    [Fact]
    public void Dado_SemConfig_Quando_Root_Entao_DefaultHomeRepos()
    {
        Create().Root.ShouldBe(Path.Join(_home, "repos"));
    }

    [Fact]
    public void Dado_RootInexistente_Quando_EnsureRoot_Entao_CriaDiretorio()
    {
        var service = Create();

        var root = service.EnsureRoot();

        Directory.Exists(root).ShouldBeTrue();
        root.ShouldBe(service.Root);
    }

    [Fact]
    public void Dado_RepoDirExiste_Quando_ResolveCardWorkdir_Entao_RetornaRepoDir()
    {
        var service = Create();
        var repoDir = Path.Join(service.Root, "agent-harness");
        Directory.CreateDirectory(repoDir);

        var path = service.ResolveCardWorkdir("afonsoft/agent-harness", out var exists);

        exists.ShouldBeTrue();
        path.ShouldBe(repoDir);
    }

    [Fact]
    public void Dado_RepoDirNaoExiste_Quando_ResolveCardWorkdir_Entao_FallbackParaRoot()
    {
        var service = Create();

        var path = service.ResolveCardWorkdir("afonsoft/nao-existe", out var exists);

        exists.ShouldBeFalse();
        path.ShouldBe(service.Root);
        Directory.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void Dado_CaminhoForaDoHome_Quando_ClampToHome_Entao_CaiNoHome()
    {
        var service = Create();

        service.ClampToHome("/etc").ShouldBe(_home);
        service.ClampToHome("../..").ShouldBe(_home);
        service.ClampToHome(null).ShouldBe(_home);
    }

    [Fact]
    public void Dado_CaminhoDentroDoHome_Quando_ClampToHome_Entao_Normaliza()
    {
        var service = Create();

        service.ClampToHome(Path.Join(_home, "repos")).ShouldBe(Path.Join(_home, "repos"));
        service.ClampToHome(_home).ShouldBe(_home);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }
}
