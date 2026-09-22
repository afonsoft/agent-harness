using Shouldly;
using Taskboard.Harness;
using Xunit;

namespace Taskboard.Tests.Unit.Harness;

/// <summary>
/// SPEC-20260919-harness-workspace-isolation: o root dos worktrees segue o
/// workspace (~/repos) por padrão e aceita Taskboard:WorktreeRoot com ~.
/// </summary>
public class WorktreePathsTests
{
    private const string Home = "/tmp/taskboard-home";

    [Fact]
    public void Dado_ConfigVazia_Quando_ResolveRoot_Entao_DefaultHomeRepos()
    {
        WorktreePaths.ResolveRoot(null, Home).ShouldBe(Path.Combine(Home, "repos"));
        WorktreePaths.ResolveRoot("  ", Home).ShouldBe(Path.Combine(Home, "repos"));
    }

    [Fact]
    public void Dado_ConfigComTilde_Quando_ResolveRoot_Entao_ExpandeHome()
    {
        WorktreePaths.ResolveRoot("~/wt", Home)
            .ShouldBe(Path.Combine(Home, "wt"));
        WorktreePaths.ResolveRoot("~", Home).ShouldBe(Home);
    }

    [Fact]
    public void Dado_ConfigAbsoluta_Quando_ResolveRoot_Entao_Normaliza()
    {
        WorktreePaths.ResolveRoot("/var/lib/./worktrees", Home)
            .ShouldBe("/var/lib/worktrees");
    }

    [Fact]
    public void Dado_Root_Quando_SessionDir_Entao_ConfinaRunIdSanitizado()
    {
        var root = Path.Combine(Home, "repos");

        WorktreePaths.SessionDir(root, "run_01").ShouldBe(Path.Combine(root, "run_01"));
        WorktreePaths.IsUnder(root, WorktreePaths.SessionDir(root, "../escape")).ShouldBeTrue();
    }
}
