using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using Shouldly;
using Taskboard.Blazor.Components.Pages;
using Taskboard.Blazor.Services;
using Taskboard.GitHub;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20260923-terminal-tabs-keyed-render RF-001/RF-002/RF-004/RF-005: ao
/// fechar uma aba à esquerda/meio, somente ela é removida e as demais mantêm
/// ordem, elementId e conteúdo. Sem @key o diff posicional do Blazor remenda
/// os nós DOM errados — o guard de source em
/// <see cref="TerminalRazorSourceGuardTests"/> é o tripwire da regressão.
/// </summary>
public class TerminalTabsTests : BunitContext
{
    public TerminalTabsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddBlazorBootstrap();

        var gitHub = Substitute.For<IGitHubService>();
        gitHub.GetRepositoriesAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RepositoryDto>());
        Services.AddSingleton(new SelectedRepositoryService(gitHub, JSInterop.JSRuntime));

        // Hub resolve via NavigationManager; porta 9 (discard) falha rápido,
        // deixando as abas em estado Error — suficiente para exercitar o DOM.
        Services.GetRequiredService<BunitNavigationManager>().NavigateTo("http://localhost:9/");
    }

    private IRenderedComponent<Terminal> RenderComTresAbas()
    {
        var cut = Render<Terminal>();
        cut.WaitForAssertion(() => cut.FindAll(".terminal-pane").Count.ShouldBe(1));
        cut.Find("button.terminal-tab-new").Click();
        cut.Find("button.terminal-tab-new").Click();
        cut.WaitForAssertion(() => cut.FindAll(".terminal-pane").Count.ShouldBe(3));
        // Aguarda as transições assíncronas de estado (Error) assentarem —
        // cada StateHasChanged pode recriar bindings de evento e tornar
        // elementos capturados obsoletos para Click().
        cut.WaitForAssertion(() => cut.FindAll(".terminal-tab")
            .ShouldAllBe(t => (t.ClassName ?? string.Empty).Contains("state-error")));
        return cut;
    }

    private static string[] HostIds(IRenderedComponent<Terminal> cut) =>
        cut.FindAll(".terminal-host").Select(h => h.Id ?? string.Empty).ToArray();

    [Fact]
    public void Dado_TresAbas_Quando_FechaAbaDoMeio_Entao_SomenteElaEhRemovida()
    {
        // Covers RF-002 + AC — fechar a aba do meio preserva os elementIds das
        // abas sobreviventes, na ordem original.
        var cut = RenderComTresAbas();
        var idsAntes = HostIds(cut);
        idsAntes.ShouldAllBe(id => id.StartsWith("terminal-host-", StringComparison.Ordinal));

        cut.FindAll(".terminal-tab-close")[1].Click();
        cut.WaitForAssertion(() => cut.FindAll(".terminal-pane").Count.ShouldBe(2));

        HostIds(cut).ShouldBe([idsAntes[0], idsAntes[2]]);
        cut.FindAll(".terminal-tab-title")
            .Select(t => t.TextContent.Trim())
            .ShouldBe(["Terminal 1", "Terminal 3"]);
    }

    [Fact]
    public void Dado_TresAbas_Quando_FechaAbaDaEsquerda_Entao_DemaisMantemOrdemEIds()
    {
        // Covers AC seção 6 — regressão exata do bug reportado.
        var cut = RenderComTresAbas();
        var idsAntes = HostIds(cut);

        cut.FindAll(".terminal-tab-close")[0].Click();
        cut.WaitForAssertion(() => cut.FindAll(".terminal-pane").Count.ShouldBe(2));

        HostIds(cut).ShouldBe([idsAntes[1], idsAntes[2]]);
        cut.FindAll(".terminal-tab-title")
            .Select(t => t.TextContent.Trim())
            .ShouldBe(["Terminal 2", "Terminal 3"]);
    }

    [Fact]
    public void Dado_TresAbas_Quando_FechaAbaAtiva_Entao_AbaSeguinteViraAtiva()
    {
        // Covers RF-004 — fechar a aba ativa ativa a que a seguia.
        var cut = RenderComTresAbas();
        var idsAntes = HostIds(cut);

        // A aba ativa é a última aberta (Terminal 3).
        cut.FindAll(".terminal-tab-close")[2].Click();
        cut.WaitForAssertion(() => cut.FindAll(".terminal-pane").Count.ShouldBe(2));

        var visivel = cut.FindAll(".terminal-pane:not(.d-none)");
        visivel.Count.ShouldBe(1);
        visivel[0].QuerySelector(".terminal-host")!.Id.ShouldBe(idsAntes[1]);
    }

    [Fact]
    public void Dado_UmaAba_Quando_Fecha_Entao_MostraEstadoVazio()
    {
        // Covers edge case — última aba removida exibe o empty state.
        var cut = Render<Terminal>();
        cut.WaitForAssertion(() => cut.FindAll(".terminal-pane").Count.ShouldBe(1));
        cut.WaitForAssertion(() => (cut.Find(".terminal-tab").ClassName ?? string.Empty).ShouldContain("state-error"));

        cut.Find(".terminal-tab-close").Click();
        cut.WaitForAssertion(() => cut.FindAll(".terminal-empty").Count.ShouldBe(1));
        cut.FindAll(".terminal-pane").Count.ShouldBe(0);
    }
}
