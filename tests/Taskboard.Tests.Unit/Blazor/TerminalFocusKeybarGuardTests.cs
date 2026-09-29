using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20260928-terminal-focus-keybar-coverage RF-001/RF-002/RF-003: tripwire
/// de regressão para os hooks do E23 (focus mode + virtual keybar). O
/// comportamento real vive na reconciliação DOM↔JS que o bUnit não observa —
/// por isso o guard atua sobre a fonte do componente, do JS e do CSS, como
/// <see cref="TerminalRazorSourceGuardTests"/>.
/// </summary>
public class TerminalFocusKeybarGuardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return dir.FullName;
    }

    private static string SourceOf(params string[] parts)
    {
        var path = Path.Join([RepoRoot(), .. parts]);
        File.Exists(path).ShouldBeTrue($"arquivo esperado não encontrado: {path}");
        return File.ReadAllText(path);
    }

    private static string TerminalRazor() =>
        SourceOf("src", "Taskboard.Blazor", "Components", "Pages", "Terminal.razor");

    private static string TerminalJs() =>
        SourceOf("src", "Taskboard.Client", "wwwroot", "js", "terminal.js");

    private static string TaskboardJs() =>
        SourceOf("src", "Taskboard.Client", "wwwroot", "js", "taskboard.js");

    private static string SiteCss() =>
        SourceOf("src", "Taskboard.Client", "wwwroot", "css", "site.css");

    [Fact]
    public void Dado_TerminalRazor_Quando_LeFonte_Entao_KeybarEhToolbar()
    {
        // Covers RF-001a — <div class="terminal-keybar" role="toolbar">.
        Regex.IsMatch(TerminalRazor(), @"class=""terminal-keybar""[^>]*role=""toolbar""")
            .ShouldBeTrue("Terminal.razor deve renderizar .terminal-keybar com role=toolbar");
    }

    [Fact]
    public void Dado_TerminalRazor_Quando_LeFonte_Entao_InvocaSetTerminalFocus()
    {
        // Covers RF-001b — interop que liga o overlay CSS html[data-terminal-focus].
        TerminalRazor().ShouldContain(
            "taskboard.setTerminalFocus",
            customMessage: "Terminal.razor deve invocar taskboard.setTerminalFocus (focus mode)");
    }

    [Fact]
    public void Dado_TerminalRazor_Quando_LeFonte_Entao_InvocaPasteClipboard()
    {
        // Covers RF-001c — botão Paste dedicado da keybar usa o helper JS.
        TerminalRazor().ShouldContain(
            "taskboardTerminal.pasteClipboard",
            customMessage: "Terminal.razor deve invocar taskboardTerminal.pasteClipboard");
    }

    [Fact]
    public void Dado_TaskboardJs_Quando_LeFonte_Entao_ExportaSetTerminalFocus()
    {
        // Covers RF-002 — window.taskboard.setTerminalFocus manipula
        // documentElement.dataset.terminalFocus.
        var js = TaskboardJs();
        js.ShouldContain("setTerminalFocus");
        js.ShouldContain("dataset.terminalFocus");
    }

    [Fact]
    public void Dado_TerminalJs_Quando_LeFonte_Entao_ExportaPasteClipboard()
    {
        // Covers RF-002 — window.taskboardTerminal.pasteClipboard é retornado
        // pelo módulo IIFE.
        var js = TerminalJs();
        js.ShouldContain("window.taskboardTerminal");
        Regex.IsMatch(js, @"async\s+function\s+pasteClipboard|pasteClipboard\s*:")
            .ShouldBeTrue("terminal.js deve implementar pasteClipboard");
    }

    [Fact]
    public void Dado_SiteCss_Quando_LeFonte_Entao_KeybarSoEmTouchEFocus()
    {
        // Covers RF-003 — a keybar é restrita a focus mode + ponteiro coarse,
        // decisão de design do E23 (não poluir desktop).
        var css = SiteCss();
        css.ShouldContain(".terminal-keybar");
        Regex.IsMatch(css, @"@media\s*\(pointer:\s*coarse\),\s*\(hover:\s*none\)")
            .ShouldBeTrue("site.css deve condicionar .terminal-keybar a (pointer: coarse)/(hover: none)");
        Regex.IsMatch(css, @"html\[data-terminal-focus\]\s*\.terminal-keybar")
            .ShouldBeTrue("site.css deve exibir .terminal-keybar apenas dentro de html[data-terminal-focus]");
    }
}
