using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20260930-mobile-responsive-ui FR-005/FR-006/FR-007 + ACs 3–7: tripwire
/// de regressão source-level para os contratos de markup/CSS/JS mobile —
/// viewport meta, modais fullscreen &lt;576px, atributos de teclado virtual e
/// o guard de campo editável dos atalhos globais. O comportamento real é
/// renderizado pelo browser; estes testes protegem os pontos de ancoragem.
/// </summary>
public class MobileResponsiveTests
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

    private static string IndexHtml() =>
        SourceOf("src", "Taskboard.Client", "wwwroot", "index.html");

    private static string SiteCss() =>
        SourceOf("src", "Taskboard.Client", "wwwroot", "css", "site.css");

    private static string TaskboardJs() =>
        SourceOf("src", "Taskboard.Client", "wwwroot", "js", "taskboard.js");

    private static IEnumerable<string> RazorFiles()
    {
        var dir = Path.Join(RepoRoot(), "src", "Taskboard.Blazor");
        return Directory.EnumerateFiles(dir, "*.razor", SearchOption.AllDirectories);
    }

    [Fact]
    public void Dado_IndexHtml_Quando_ParseViewport_Entao_ContemInteractiveWidget()
    {
        var html = IndexHtml();
        var viewport = Regex.Match(html, @"<meta\s+name=""viewport""\s+content=""([^""]+)""");
        viewport.Success.ShouldBeTrue("index.html deve ter meta viewport");
        viewport.Groups[1].Value.ShouldContain("interactive-widget=resizes-content");
        viewport.Groups[1].Value.ShouldContain("width=device-width");
    }

    [Fact]
    public void Dado_IndexHtml_Quando_ParseViewport_Entao_NaoDesabilitaZoomUsuario()
    {
        var html = IndexHtml();
        html.ShouldNotContain("user-scalable=no");
        html.ShouldNotContain("maximum-scale");
    }

    [Fact]
    public void Dado_Modals_Quando_ParseRazor_Entao_UsamModalFullscreenSmDown()
    {
        var missing = new List<string>();
        foreach (var file in RazorFiles())
        {
            var source = File.ReadAllText(file);
            foreach (var m in Regex.Matches(source, @"<Modal\b[^>]*>").Where(m => !m.Value.Contains("Fullscreen=\"ModalFullscreen.SmallDown\"")))
            {
                missing.Add($"{Path.GetFileName(file)}: {m.Value[..Math.Min(m.Value.Length, 80)]}");
            }
        }

        missing.ShouldBeEmpty(
            "todo <Modal> deve usar Fullscreen=\"ModalFullscreen.SmallDown\" (FR-005):\n" +
            string.Join('\n', missing));
    }

    [Fact]
    public void Dado_InputsMobile_Quando_ParseRazor_Entao_TemInputmodeOuAutocompleteAdequado()
    {
        var settings = SourceOf("src", "Taskboard.Blazor", "Components", "Pages", "Settings.razor");

        // FR-006: campos de URL usam inputmode="url".
        foreach (var id in new[] { "rag-url", "chat-provider-url", "chat-search-url" })
        {
            var field = Regex.Match(settings, $@"<input[^>]*id=""{id}""[^>]*>");
            field.Success.ShouldBeTrue($"input #{id} esperado em Settings.razor");
            field.Value.ShouldContain("inputmode=\"url\"");
        }

        // FR-006: campos de key/token desabilitam autocomplete/correção.
        foreach (var id in new[] { "github-token", "rag-key", "chat-provider-key", "chat-search-key" })
        {
            var field = Regex.Match(settings, $@"<input[^>]*id=""{id}""[^>]*>");
            field.Success.ShouldBeTrue($"input #{id} esperado em Settings.razor");
            field.Value.ShouldContain("autocomplete=\"off\"");
            field.Value.ShouldContain("autocapitalize=\"none\"");
            field.Value.ShouldContain("spellcheck=\"false\"");
        }

        // Busca global usa enterkeyhint="search" e expõe alvo do atalho `s`.
        var searchCount = Regex.Matches(
            string.Join('\n', RazorFiles().Select(File.ReadAllText)),
            @"enterkeyhint=""search""").Count;
        searchCount.ShouldBeGreaterThanOrEqualTo(3);

        // Login preserva os hints de credencial.
        var login = SourceOf("src", "Taskboard.Blazor", "Components", "Pages", "Login.razor");
        login.ShouldContain("autocomplete=\"username\"");
        login.ShouldContain("autocomplete=\"current-password\"");
    }

    [Fact]
    public void Dado_TaskboardJs_Quando_ParseShortcuts_Entao_ExisteGuardDeCampoEditavel()
    {
        var js = TaskboardJs();

        // FR-007: módulo único + listener único em document. O chat tem um
        // capture-phase listener próprio (SPEC-20261017: Ctrl+K ganha do
        // shortcut global e dispara mesmo com o composer focado) — um por módulo.
        js.ShouldContain("window.taskboardShortcuts");
        js.ShouldContain("window.taskboardChat");
        Regex.Matches(js, @"document\.addEventListener\('keydown'").Count.ShouldBe(2);

        // Guard cobre campos editáveis, xterm e editores de código.
        var guard = Regex.Match(js, @"_isEditable:\s*function[^}]+}");
        guard.Success.ShouldBeTrue("taskboardShortcuts deve ter guard _isEditable");
        foreach (var sel in new[] { "input", "textarea", "select", "contenteditable", "xterm-helper-textarea" })
        {
            guard.Value.ShouldContain(sel);
        }

        // AC-06/AC-07: overlay ? existe; Esc fecha; preventDefault só em ação real.
        js.ShouldContain("shortcut-help-overlay");
        js.ShouldContain("e.key === 'Escape'");
        js.ShouldContain("'new'");
        js.ShouldContain("'search'");
    }

    [Fact]
    public void Dado_SiteCss_Quando_ParseMediaQueries_Entao_CobreContratosMobile()
    {
        var css = SiteCss();

        // FR-002: alvos de toque >= 44px sob ponteiro impreciso.
        var coarse = Regex.Match(css, @"@media \(pointer: coarse\)[^{]*\{(?<body>.*?)\n\}", RegexOptions.Singleline);
        coarse.Success.ShouldBeTrue();
        css.ShouldContain("@media (pointer: coarse), (hover: none)");
        css.ShouldContain("min-height: 44px");

        // FR-003: inputs >= 16px abaixo de 768px (guard de zoom do iOS).
        css.ShouldContain("@media (max-width: 767.98px)");

        // FR-001: trava de overflow-x do body em telas estreitas.
        css.ShouldContain("overflow-x: clip");

        // FR-007: overlay de atalhos estilizado.
        css.ShouldContain(".shortcut-help-overlay");
    }
}
