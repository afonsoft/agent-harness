using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20261010-agents-page-tabs: tripwire de estrutura do Agents.razor —
/// quatro abas ARIA (Dashboard default, Sessions, CLI Agents, Prompt), panes
/// preservados no DOM via `hidden=`, picker de CLI antes da tabela de sessões
/// (take=30) e deep link `?tab=`/`?cli=`.
/// </summary>
public class AgentsTabsTests
{
    private static string AgentsRazorPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return Path.Join(dir.FullName, "src", "Taskboard.Blazor", "Components", "Pages", "Agents.razor");
    }

    private static string Source() => File.ReadAllText(AgentsRazorPath());

    [Fact]
    public void Dado_AgentsRazor_Quando_LeFonte_Entao_QuatroAbasNaOrdem()
    {
        var source = Source();

        source.ShouldContain("""role="tablist" """.TrimEnd());
        source.ShouldContain("data-tab=\"@id\"");
        var dashboard = source.IndexOf("(\"dashboard\", \"Dashboard\")", StringComparison.Ordinal);
        var sessions = source.IndexOf("(\"sessions\", \"Sessions\")", StringComparison.Ordinal);
        var cliAgents = source.IndexOf("(\"cli-agents\", \"CLI Agents\")", StringComparison.Ordinal);
        var prompt = source.IndexOf("(\"prompt\", \"Prompt\")", StringComparison.Ordinal);

        dashboard.ShouldBeGreaterThanOrEqualTo(0);
        sessions.ShouldBeGreaterThan(dashboard);
        cliAgents.ShouldBeGreaterThan(sessions);
        prompt.ShouldBeGreaterThan(cliAgents);
    }

    [Fact]
    public void Dado_AgentsRazor_Quando_LeFonte_Entao_PanesNoDomViaHidden()
    {
        var source = Source();

        source.ShouldContain("""hidden="@(_activeTab != "dashboard")" """.TrimEnd());
        source.ShouldContain("""hidden="@(_activeTab != "sessions")" """.TrimEnd());
        source.ShouldContain("""hidden="@(_activeTab != "cli-agents")" """.TrimEnd());
        source.ShouldContain("""hidden="@(_activeTab != "prompt")" """.TrimEnd());
    }

    [Fact]
    public void Dado_AgentsRazor_Quando_LeFonte_Entao_AriaSelectedTernario()
    {
        // Blazor descarta aria-* quando o bool bound é false — usar ternário.
        var source = Source();

        source.ShouldContain("aria-selected=\"@(id == _activeTab ? \"true\" : \"false\")\"");
        source.ShouldContain("aria-pressed=\"@(id == _selectedCli ? \"true\" : \"false\")\"");
    }

    [Fact]
    public void Dado_AgentsRazor_Quando_LeFonte_Entao_DeepLinkTabECli()
    {
        var source = Source();

        source.ShouldContain("ResolveQueryValue(\"tab\")");
        source.ShouldContain("ResolveQueryValue(\"cli\")");
        source.ShouldContain("\"/agents?tab=sessions&cli={cli}\"");
        source.ShouldContain("replace: true");
    }

    [Fact]
    public void Dado_AgentsRazor_Quando_LeFonte_Entao_SessionsPickerAntesDaTabela()
    {
        var source = Source();
        var paneStart = source.IndexOf("aria-label=\"Sessions\"", StringComparison.Ordinal);
        var paneEnd = source.IndexOf("aria-label=\"CLI Agents\"", StringComparison.Ordinal);
        paneStart.ShouldBeGreaterThanOrEqualTo(0);
        paneEnd.ShouldBeGreaterThan(paneStart);

        var pane = source[paneStart..paneEnd];
        var picker = pane.IndexOf("SessionSources", StringComparison.Ordinal);
        var table = pane.IndexOf("<table", StringComparison.Ordinal);
        picker.ShouldBeGreaterThanOrEqualTo(0);
        table.ShouldBeGreaterThan(picker);
        pane.ShouldContain("SelectSessionCliAsync");
        pane.ShouldContain("_selectedCli is null");
    }

    [Fact]
    public void Dado_AgentsRazor_Quando_LeFonte_Entao_Take30ESeisFontes()
    {
        var source = Source();

        source.ShouldContain("SessionsTake = 30");
        source.ShouldContain("take: SessionsTake");
        source.ShouldContain("\"claude\", \"codex\", \"opencode\", \"gemini\", \"agy\", \"devin\"");
    }

    [Fact]
    public void Dado_AgentsRazor_Quando_LeFonte_Entao_SecoesNosPanesCorretos()
    {
        var source = Source();
        var dashboard = source.IndexOf("aria-label=\"Dashboard\"", StringComparison.Ordinal);
        var sessions = source.IndexOf("aria-label=\"Sessions\"", StringComparison.Ordinal);
        var cliAgents = source.IndexOf("aria-label=\"CLI Agents\"", StringComparison.Ordinal);
        var prompt = source.IndexOf("aria-label=\"Prompt\"", StringComparison.Ordinal);

        var agentDashboard = source.IndexOf("Agent Dashboard", StringComparison.Ordinal);
        var cliSessions = source.IndexOf("CLI Sessions", StringComparison.Ordinal);
        var customClis = source.IndexOf("Custom CLIs", StringComparison.Ordinal);
        var defaultPrompt = source.IndexOf("Default agent prompt", StringComparison.Ordinal);

        agentDashboard.ShouldBeGreaterThan(dashboard);
        agentDashboard.ShouldBeLessThan(sessions);
        cliSessions.ShouldBeGreaterThan(sessions);
        cliSessions.ShouldBeLessThan(cliAgents);
        customClis.ShouldBeGreaterThan(cliAgents);
        customClis.ShouldBeLessThan(prompt);
        defaultPrompt.ShouldBeGreaterThan(prompt);
    }
}
