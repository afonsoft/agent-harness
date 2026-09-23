using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20260923-terminal-tabs-keyed-render RF-001/RF-002/RF-005: tripwire de
/// regressão — os dois <c>@foreach</c> sobre <c>_tabs</c> em Terminal.razor
/// devem declarar <c>@key="tab"</c>. O bug se manifesta apenas na reconciliação
/// DOM↔JS (xterm vive num Map fora do DOM do Blazor), que o bUnit não consegue
/// observar — por isso o guard atua sobre a fonte do componente.
/// </summary>
public class TerminalRazorSourceGuardTests
{
    private static string TerminalRazorPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return Path.Join(dir.FullName, "src", "Taskboard.Blazor", "Components", "Pages", "Terminal.razor");
    }

    private static int CountMatches(string source, string pattern) =>
        Regex.Matches(source, pattern).Count;

    [Fact]
    public void Dado_TerminalRazor_Quando_LeFonte_Entao_TabstripTemKeyNaAba()
    {
        // Covers RF-001 — <li class="nav-item" @key="tab"> na tabstrip.
        var source = File.ReadAllText(TerminalRazorPath());
        CountMatches(source, @"<li\s+class=""nav-item""[^>]*@key=""tab""").ShouldBe(1);
    }

    [Fact]
    public void Dado_TerminalRazor_Quando_LeFonte_Entao_PainelTemKeyNaAba()
    {
        // Covers RF-002 — <div class="terminal-pane ..." @key="tab"> ancora o
        // host xterm; sem ele o nó removido é sempre o último, desanexando o
        // terminal errado.
        var source = File.ReadAllText(TerminalRazorPath());
        CountMatches(source, @"<div\s+class=""terminal-pane[^""]*""[^>]*@key=""tab""").ShouldBe(1);
    }

    [Fact]
    public void Dado_TerminalJs_Quando_LeFonte_Entao_InitEhIdempotente()
    {
        // Covers RF-003 — init/initReadOnly dispõem entrada pré-existente do
        // mesmo elementId antes de recriar a instância xterm.
        var razor = TerminalRazorPath();
        var jsPath = Path.GetFullPath(Path.Join(
            Path.GetDirectoryName(razor)!, "..", "..", "..",
            "Taskboard.Client", "wwwroot", "js", "terminal.js"));
        var source = File.ReadAllText(jsPath);

        var initBody = Regex.Match(source, @"function init\(elementId,[^)]*\)\s*\{(?<body>.*?)terms\.set", RegexOptions.Singleline);
        initBody.Success.ShouldBeTrue();
        initBody.Groups["body"].Value.ShouldContain("dispose(elementId)");

        var roBody = Regex.Match(source, @"function initReadOnly\(elementId\)\s*\{(?<body>.*?)terms\.set", RegexOptions.Singleline);
        roBody.Success.ShouldBeTrue();
        roBody.Groups["body"].Value.ShouldContain("dispose(elementId)");
    }
}
