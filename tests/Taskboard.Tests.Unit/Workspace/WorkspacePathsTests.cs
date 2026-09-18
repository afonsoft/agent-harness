using Shouldly;
using Taskboard.Workspace;
using Xunit;

namespace Taskboard.Tests.Unit.Workspace;

public class WorkspacePathsTests
{
    private const string Home = "/tmp/taskboard-home";

    [Fact]
    public void Dado_ConfigVazia_Quando_ResolveRoot_Entao_DefaultHomeRepos()
    {
        WorkspacePaths.ResolveRoot(null, Home).ShouldBe(Path.Combine(Home, "repos"));
        WorkspacePaths.ResolveRoot("  ", Home).ShouldBe(Path.Combine(Home, "repos"));
    }

    [Fact]
    public void Dado_ConfigComTilde_Quando_ResolveRoot_Entao_ExpandeHome()
    {
        WorkspacePaths.ResolveRoot("~/workspaces", Home)
            .ShouldBe(Path.Combine(Home, "workspaces"));
    }

    [Fact]
    public void Dado_ConfigAbsoluta_Quando_ResolveRoot_Entao_Normaliza()
    {
        WorkspacePaths.ResolveRoot("/var/lib/./tasks", Home)
            .ShouldBe("/var/lib/tasks");
    }

    [Fact]
    public void Dado_NomeValido_Quando_SanitizeRepoName_Entao_UltimoSegmento()
    {
        WorkspacePaths.SanitizeRepoName("afonsoft/taskboard-ai").ShouldBe("taskboard-ai");
        WorkspacePaths.SanitizeRepoName("taskboard_ai.v2").ShouldBe("taskboard_ai.v2");
    }

    [Fact]
    public void Dado_NomeComCaracteresInvalidos_Quando_SanitizeRepoName_Entao_SubstituiPorTraco()
    {
        WorkspacePaths.SanitizeRepoName("owner/repo name:1").ShouldBe("repo-name-1");
        WorkspacePaths.SanitizeRepoName("owner/re$p@ç").ShouldBe("re-p--");
    }

    [Fact]
    public void Dado_NomeVazioOuTraversal_Quando_SanitizeRepoName_Entao_Vazio()
    {
        WorkspacePaths.SanitizeRepoName(null).ShouldBeEmpty();
        WorkspacePaths.SanitizeRepoName("").ShouldBeEmpty();
        WorkspacePaths.SanitizeRepoName("..").ShouldBeEmpty();
        WorkspacePaths.SanitizeRepoName("owner/..").ShouldBeEmpty();
    }

    [Fact]
    public void Dado_TraversalNoNome_Quando_RepoWorkdir_Entao_NaoEscapaDoRoot()
    {
        var root = Path.Combine(Home, "repos");

        WorkspacePaths.RepoWorkdir(root, "../../etc").ShouldBe(Path.Combine(root, "etc"));
        WorkspacePaths.RepoWorkdir(root, "..\\..\\windows").ShouldBe(Path.Combine(root, "-..-windows"));
        WorkspacePaths.IsUnder(root, WorkspacePaths.RepoWorkdir(root, "../../evil")).ShouldBeTrue();
    }

    [Fact]
    public void Dado_Caminhos_Quando_IsUnder_Entao_ConfinaNoRoot()
    {
        var root = Path.Combine(Home, "repos");

        WorkspacePaths.IsUnder(root, Path.Combine(root, "repo")).ShouldBeTrue();
        WorkspacePaths.IsUnder(root, root).ShouldBeTrue();
        WorkspacePaths.IsUnder(root, Path.Combine(root, "..", "outside")).ShouldBeFalse();
        WorkspacePaths.IsUnder(root, "/etc/passwd").ShouldBeFalse();
    }
}
