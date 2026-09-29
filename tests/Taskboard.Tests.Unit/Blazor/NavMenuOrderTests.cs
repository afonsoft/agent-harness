using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20260928-nav-menu-order RF-001: a ordem dos NavLinks é contrato —
/// Board, AI Code, Terminal, Gantt, Workflow, Specs, VS Code no topo;
/// divider; Cockpit, CLI Agents, FinOps, Settings, Skills, Prompts, Issues.
/// PR #350 mudou a ordem sem cobertura (SPEC-20260929-quality-hygiene RF-006).
/// </summary>
public class NavMenuOrderTests
{
    private static string NavMenu()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        var path = Path.Join(dir.FullName, "src", "Taskboard.Blazor", "Layout", "NavMenu.razor");
        File.Exists(path).ShouldBeTrue($"NavMenu.razor não encontrado: {path}");
        return File.ReadAllText(path);
    }

    private static int PositionOf(string source, string href) =>
        source.IndexOf($"href=\"{href}\"", StringComparison.Ordinal);

    [Fact]
    public void Dado_NavMenu_Quando_LeFonte_Entao_OrdemPrincipalFixada()
    {
        var src = NavMenu();

        var board = PositionOf(src, "");
        var aiChat = PositionOf(src, "ai-chat");
        var terminal = PositionOf(src, "terminal");
        var gantt = PositionOf(src, "gantt");
        var workflow = PositionOf(src, "workflow");
        var specs = PositionOf(src, "specs");
        var editor = PositionOf(src, "editor");

        board.ShouldBeGreaterThanOrEqualTo(0);
        aiChat.ShouldBeGreaterThan(board, "AI Code deve vir logo após Board");
        terminal.ShouldBeGreaterThan(aiChat, "Terminal deve vir após AI Code");
        gantt.ShouldBeGreaterThan(terminal);
        workflow.ShouldBeGreaterThan(gantt);
        specs.ShouldBeGreaterThan(workflow);
        editor.ShouldBeGreaterThan(specs);
    }

    [Fact]
    public void Dado_NavMenu_Quando_LeFonte_Entao_SegundaSecaoAposDivider()
    {
        var src = NavMenu();

        // Divider (<hr>) separa as seções; segunda seção começa no Cockpit.
        var divider = src.IndexOf("<hr", StringComparison.Ordinal);
        divider.ShouldBeGreaterThanOrEqualTo(0, "NavMenu deve ter um divider entre as seções");

        var editor = PositionOf(src, "editor");
        var cockpit = PositionOf(src, "cockpit");
        var agents = PositionOf(src, "agents");
        var finops = PositionOf(src, "finops");
        var settings = PositionOf(src, "settings");
        var skills = PositionOf(src, "skills");
        var prompts = PositionOf(src, "prompts/taskboard/manage-taskboard");
        var issues = src.IndexOf("github.com/afonsoft/agent-harness/issues", StringComparison.Ordinal);

        divider.ShouldBeGreaterThan(editor, "divider deve ficar após a última entrada da primeira seção (VS Code)");
        cockpit.ShouldBeGreaterThan(divider);
        agents.ShouldBeGreaterThan(cockpit);
        finops.ShouldBeGreaterThan(agents);
        settings.ShouldBeGreaterThan(finops);
        skills.ShouldBeGreaterThan(settings);
        prompts.ShouldBeGreaterThan(skills);
        issues.ShouldBeGreaterThan(prompts);
    }
}
