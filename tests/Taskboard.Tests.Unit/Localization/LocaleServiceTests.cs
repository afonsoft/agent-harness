using System.Globalization;
using Microsoft.JSInterop;
using NSubstitute.Core;
using NSubstitute;
using Shouldly;
using Taskboard.Blazor.Localization;
using Taskboard.Blazor.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Localization;

/// <summary>
/// SPEC-20261008-locale-picker: paridade das tabelas UiStrings e o serviço de
/// locale (fallback, persistência, evento Changed, normalização de cultura).
/// </summary>
public class LocaleServiceTests : IDisposable
{
    private readonly CultureInfo _savedCulture = CultureInfo.DefaultThreadCurrentCulture ?? CultureInfo.InvariantCulture;
    private readonly CultureInfo _savedUiCulture = CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.InvariantCulture;

    [Fact]
    public void Dado_AsTresCulturas_Quando_CompararChaves_Entao_MesmoConjunto()
    {
        var pt = UiStrings.For("pt-BR");
        var en = UiStrings.For("en");
        var es = UiStrings.For("es");

        en.Keys.Order().ShouldBe(pt.Keys.Order());
        es.Keys.Order().ShouldBe(pt.Keys.Order());
        pt.Values.ShouldAllBe(v => !string.IsNullOrWhiteSpace(v));
        en.Values.ShouldAllBe(v => !string.IsNullOrWhiteSpace(v));
        es.Values.ShouldAllBe(v => !string.IsNullOrWhiteSpace(v));
    }

    [Fact]
    public void Dado_CulturaQualquer_Quando_Get_Entao_RespectivaTraducao()
    {
        UiStrings.Get("pt-BR", "topbar.logout").ShouldBe("Sair");
        UiStrings.Get("en", "topbar.logout").ShouldBe("Log out");
        UiStrings.Get("es", "topbar.logout").ShouldBe("Cerrar sesión");
    }

    [Fact]
    public void Dado_CulturaDesconhecida_Quando_Get_Entao_FallbackPtBr()
    {
        UiStrings.Get("fr", "topbar.logout").ShouldBe("Sair");
    }

    [Fact]
    public void Dado_ChaveAusente_Quando_Get_Entao_DevolveAPropriaChave()
    {
        UiStrings.Get("en", "nao.existe.essa.chave").ShouldBe("nao.existe.essa.chave");
    }

    [Fact]
    public async Task Dado_ServicoNovo_Quando_Padrao_Entao_PtBr()
    {
        var service = new LocaleService(Substitute.For<IJSRuntime>());
        service.Culture.ShouldBe("pt-BR");
        service.T("nav.settings").ShouldBe("Configurações");
    }

    [Fact]
    public async Task Dado_PersistenciaEs_Quando_InitializeAsync_Entao_AplicaENotifica()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<string?>("taskboard.getLocale", Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
            .Returns(new ValueTask<string?>("es"));
        var service = new LocaleService(js);
        var fired = 0;
        service.Changed += () => fired++;

        await service.InitializeAsync();

        service.Culture.ShouldBe("es");
        service.T("topbar.logout").ShouldBe("Cerrar sesión");
        fired.ShouldBe(1);
        CultureInfo.CurrentCulture.Name.ShouldBe("es");
    }

    [Fact]
    public async Task Dado_InitializeDuasVezes_Quando_InitializeAsync_Entao_LeUmaVez()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<string?>("taskboard.getLocale", Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
            .Returns(new ValueTask<string?>("en"));
        var service = new LocaleService(js);

        await service.InitializeAsync();
        await service.InitializeAsync();

        await js.Received(1).InvokeAsync<string?>(
            "taskboard.getLocale", Arg.Any<CancellationToken>(), Arg.Any<object?[]?>());
    }

    [Fact]
    public async Task Dado_TrocaParaEn_Quando_SetCultureAsync_Entao_PersisteAplicaENotifica()
    {
        var js = Substitute.For<IJSRuntime>();
        var service = new LocaleService(js);
        var fired = 0;
        service.Changed += () => fired++;

        await service.SetCultureAsync("en");

        service.Culture.ShouldBe("en");
        fired.ShouldBe(1);
        CultureInfo.CurrentUICulture.Name.ShouldBe("en");
        var identifiers = js.ReceivedCalls()
            .Select(c => c.GetArguments()[0] as string)
            .ToList();
        identifiers.ShouldContain("taskboard.setLocale");
        identifiers.ShouldContain("taskboard.setHtmlLang");
    }

    [Fact]
    public async Task Dado_AliasEnUs_Quando_SetCultureAsync_Entao_Normaliza()
    {
        var service = new LocaleService(Substitute.For<IJSRuntime>());

        await service.SetCultureAsync("en-US");

        service.Culture.ShouldBe("en");
    }

    [Fact]
    public async Task Dado_CulturaInvalida_Quando_SetCultureAsync_Entao_Ignora()
    {
        var service = new LocaleService(Substitute.For<IJSRuntime>());
        var fired = 0;
        service.Changed += () => fired++;

        await service.SetCultureAsync("fr");

        service.Culture.ShouldBe("pt-BR");
        fired.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_TComArgs_Quando_Formata_Entao_Interpola()
    {
        var service = new LocaleService(Substitute.For<IJSRuntime>());

        service.T("board.issueActions", 42).ShouldBe("Ações da issue #42");

        await service.SetCultureAsync("en");
        service.T("board.issueActions", 42).ShouldBe("Actions for issue #42");
    }

    public void Dispose()
    {
        CultureInfo.DefaultThreadCurrentCulture = _savedCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _savedUiCulture;
    }
}
