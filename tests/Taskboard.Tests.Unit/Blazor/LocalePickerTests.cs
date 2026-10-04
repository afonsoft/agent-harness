using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20261008-locale-picker: tripwire source-level para o combo de idiomas —
/// o MainLayout hospeda o picker à direita do título, o MinimalLayout também
/// expõe (login), e o taskboard.js carrega as três funções de locale.
/// </summary>
public class LocalePickerTests
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

    private static string SourceOf(params string[] parts) =>
        File.ReadAllText(Path.Join([RepoRoot(), .. parts]));

    [Fact]
    public void Dado_MainLayout_Quando_ParseRazor_Entao_HospedaLocalePickerNoTopbar()
    {
        var source = SourceOf("src", "Taskboard.Blazor", "Layout", "MainLayout.razor");

        var title = source.IndexOf("app-topbar-title", StringComparison.Ordinal);
        var picker = source.IndexOf("<LocalePicker", StringComparison.Ordinal);
        var settings = source.IndexOf("href=\"/settings\"", StringComparison.Ordinal);
        (title >= 0 && picker > title && settings > picker)
            .ShouldBeTrue("LocalePicker deve ficar no topbar entre o título e o botão de Settings");
    }

    [Fact]
    public void Dado_MinimalLayout_Quando_ParseRazor_Entao_TambemExpoePicker()
    {
        var source = SourceOf("src", "Taskboard.Blazor", "Layout", "MinimalLayout.razor");
        source.ShouldContain("<LocalePicker");
    }

    [Fact]
    public void Dado_TaskboardJs_Quando_Parse_Entao_ExpoeLocaleApi()
    {
        var source = SourceOf("src", "Taskboard.Client", "wwwroot", "js", "taskboard.js");
        source.ShouldContain("getLocale");
        source.ShouldContain("setLocale");
        source.ShouldContain("setHtmlLang");
        source.ShouldContain("harness.locale");
    }

    [Fact]
    public void Dado_LocalePicker_Quando_ParseFontes_Entao_ListaTresCulturas()
    {
        var strings = SourceOf("src", "Taskboard.Blazor", "Localization", "UiStrings.cs");
        strings.ShouldContain("\"pt-BR\"");
        strings.ShouldContain("\"en\"");
        strings.ShouldContain("\"es\"");

        var service = SourceOf("src", "Taskboard.Blazor", "Services", "LocaleService.cs");
        service.ShouldContain("Português");
        service.ShouldContain("English");
        service.ShouldContain("Español");
    }
}
