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

    // SPEC-20260929-quality-hygiene RF-003/RF-004/RF-005 — guards estruturais:
    // não basta o nome existir na fonte, ele precisa estar no objeto exportado
    // / dentro do bloco de media query / ligado ao handler correto.

    /// <summary>Extrai o bloco {...} que começa em <paramref name="openBraceAt"/>.</summary>
    private static string BlockAt(string source, int openBraceAt)
    {
        var depth = 0;
        for (var i = openBraceAt; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
            {
                return source[openBraceAt..(i + 1)];
            }
        }

        throw new InvalidOperationException("bloco sem fechamento");
    }

    [Fact]
    public void Dado_TerminalJs_Quando_LeFonte_Entao_PasteClipboardNoObjetoExportado()
    {
        // RF-003 — pasteClipboard deve constar no objeto retornado pelo IIFE
        // (o objeto que vira window.taskboardTerminal), não apenas declarado.
        var js = TerminalJs();
        var returnKw = js.LastIndexOf("return", StringComparison.Ordinal);
        var brace = js.IndexOf('{', returnKw);
        var exports = BlockAt(js, brace);

        exports.ShouldContain("pasteClipboard");
        exports.ShouldContain("initReadOnly"); // export usado pelo pane read-only
    }

    [Fact]
    public void Dado_TaskboardJs_Quando_LeFonte_Entao_SetTerminalFocusNoObjetoExportado()
    {
        // RF-003 — setTerminalFocus deve ser propriedade de window.taskboard.
        var js = TaskboardJs();
        var assign = js.IndexOf("window.taskboard =", StringComparison.Ordinal);
        assign.ShouldBeGreaterThanOrEqualTo(0);
        var brace = js.IndexOf('{', assign);
        var exports = BlockAt(js, brace);

        Regex.IsMatch(exports, @"setTerminalFocus\s*:")
            .ShouldBeTrue("setTerminalFocus deve ser membro do objeto window.taskboard");
    }

    [Fact]
    public void Dado_SiteCss_Quando_LeFonte_Entao_KeybarDentroDoBlocoMedia()
    {
        // RF-004 — a regra de visibilidade da keybar precisa viver DENTRO do
        // bloco @media (pointer: coarse)/(hover: none), não apenas coexistir.
        var css = SiteCss();
        var media = Regex.Match(css, @"@media\s*\(pointer:\s*coarse\),\s*\(hover:\s*none\)");
        media.Success.ShouldBeTrue("media query de ponteiro coarse não encontrada");
        var brace = css.IndexOf('{', media.Index);
        var block = BlockAt(css, brace);

        Regex.IsMatch(block, @"html\[data-terminal-focus\]\s*\.terminal-keybar")
            .ShouldBeTrue("a regra html[data-terminal-focus] .terminal-keybar deve estar dentro do bloco da media query");
    }

    [Fact]
    public void Dado_TerminalRazor_Quando_LeFonte_Entao_HandlerDeFocoLigaAoEstado()
    {
        // RF-005 — o handler ToggleFocusModeAsync é o único dono do flip de
        // _focusMode e deve acionar o interop que aplica o atributo no <html>.
        var razor = TerminalRazor();
        var handler = razor.IndexOf("ToggleFocusModeAsync()", StringComparison.Ordinal);
        handler.ShouldBeGreaterThanOrEqualTo(0);
        var brace = razor.IndexOf('{', handler);
        var body = BlockAt(razor, brace);

        body.ShouldContain("_focusMode");
        body.ShouldContain("taskboard.setTerminalFocus");
    }

    // SPEC-20261001-terminal-memory-mobile — guards da memória adaptativa e
    // do toggle de keybar fora do focus mode.

    [Fact]
    public void Dado_TerminalJs_Quando_LeFonte_Entao_ScrollbackAdaptativo()
    {
        // RF-001 — resolveScrollback com defaults desktop/mobile por perfil.
        var js = TerminalJs();
        js.ShouldContain("resolveScrollback");
        js.ShouldContain("desktop: 2000");
        js.ShouldContain("mobile: 800");
        js.ShouldContain("desktop: 3000");
        js.ShouldContain("mobile: 1200");
    }

    [Fact]
    public void Dado_TerminalJs_Quando_LeFonte_Entao_ScrollPreservadoAposFit()
    {
        // RF-005 — após fit válido, terminal no fim volta ao fim
        // (scrollToBottomIfPinned), quem subiu mantém a posição.
        var js = TerminalJs();
        js.ShouldContain("scrollToBottomIfPinned");
        Regex.IsMatch(js, @"function\s+reportResize[\s\S]*?scrollToBottomIfPinned\(entry\)")
            .ShouldBeTrue("reportResize deve ancorar no fim após o fit");
    }

    [Fact]
    public void Dado_TerminalRazor_Quando_LeFonte_Entao_ToggleKeybarForaDoFocus()
    {
        // RF-003 — toggle do teclado virtual na tabstrip com aria-pressed e
        // interop que liga html[data-terminal-keybar].
        var razor = TerminalRazor();
        razor.ShouldContain("terminal-keybar-toggle");
        razor.ShouldContain("taskboard.setTerminalKeybar");
        razor.ShouldContain("aria-pressed=\"@_keybarVisible\"");
    }

    [Fact]
    public void Dado_TaskboardJs_Quando_LeFonte_Entao_ExportaSetTerminalKeybar()
    {
        // RF-003 — window.taskboard.setTerminalKeybar manipula
        // documentElement.dataset.terminalKeybar.
        var js = TaskboardJs();
        var assign = js.IndexOf("window.taskboard =", StringComparison.Ordinal);
        assign.ShouldBeGreaterThanOrEqualTo(0);
        var brace = js.IndexOf('{', assign);
        var exports = BlockAt(js, brace);

        Regex.IsMatch(exports, @"setTerminalKeybar\s*:")
            .ShouldBeTrue("setTerminalKeybar deve ser membro do objeto window.taskboard");
        js.ShouldContain("dataset.terminalKeybar");
    }

    [Fact]
    public void Dado_SiteCss_Quando_LeFonte_Entao_KeybarVisivelComAtributoKeybar()
    {
        // RF-003 — a keybar aparece em coarse pointer também com
        // html[data-terminal-keybar], não só em focus mode.
        var css = SiteCss();
        var media = Regex.Match(css, @"@media\s*\(pointer:\s*coarse\),\s*\(hover:\s*none\)");
        media.Success.ShouldBeTrue("media query de ponteiro coarse não encontrada");
        var brace = css.IndexOf('{', media.Index);
        var block = BlockAt(css, brace);

        Regex.IsMatch(block, @"html\[data-terminal-keybar\]\s*\.terminal-keybar")
            .ShouldBeTrue("a regra html[data-terminal-keybar] .terminal-keybar deve estar dentro do bloco da media query");
        Regex.IsMatch(block, @"\.terminal-keybar-toggle")
            .ShouldBeTrue("o toggle .terminal-keybar-toggle só deve aparecer em coarse pointer");
    }

    [Fact]
    public void Dado_SiteCss_Quando_LeFonte_Entao_TabstripCompactaEmMobile()
    {
        // RF-004 — abaixo de 768px: scroll-snap na tabstrip, títulos
        // truncados e host mais baixo; descrição some abaixo de 576px.
        var css = SiteCss();
        var media = Regex.Match(css, @"@media\s*\(max-width:\s*767\.98px\)");
        media.Success.ShouldBeTrue("media query mobile do terminal não encontrada");
        var brace = css.IndexOf('{', media.Index);
        var block = BlockAt(css, brace);

        block.ShouldContain("scroll-snap-type: x proximity");
        block.ShouldContain("min-height: 160px");
        css.ShouldContain("@media (max-width: 575.98px)");
    }
}
