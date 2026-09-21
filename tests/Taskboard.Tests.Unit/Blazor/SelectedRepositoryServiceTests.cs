using Microsoft.JSInterop;
using NSubstitute;
using Shouldly;
using Taskboard.Blazor.Services;
using Taskboard.GitHub;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20260920-global-repo-selector RF-001: seleção única de repositório
/// compartilhada pelas telas repo-scoped, persistida em localStorage.
/// </summary>
public class SelectedRepositoryServiceTests
{
    private static readonly RepositoryDto[] Repos =
    [
        new(1, "octokit/octokit.net", "octokit.net", null, "https://x", false),
        new(2, "afonsoft/agent-harness", "agent-harness", null, "https://x", true),
        new(3, "afonsoft/skills", "skills", null, "https://x", false),
    ];

    private static SelectedRepositoryService CreateService(
        IGitHubService gitHub, IJSRuntime js) => new(gitHub, js);

    private static (SelectedRepositoryService Service, IGitHubService GitHub, IJSRuntime Js) Create(
        string? persisted = null)
    {
        var gitHub = Substitute.For<IGitHubService>();
        gitHub.GetRepositoriesAsync(Arg.Any<CancellationToken>()).Returns(Repos);
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<string?>("taskboard.getSelectedRepo", Arg.Any<object?[]?>())
            .Returns(new ValueTask<string?>(persisted));
        return (CreateService(gitHub, js), gitHub, js);
    }

    [Fact]
    public async Task Dado_SemValorPersistido_Quando_EnsureLoaded_Entao_SelecionaPrimeiroAlfabetico()
    {
        var (service, _, _) = Create();
        await service.EnsureLoadedAsync();
        service.Selected.ShouldBe("afonsoft/skills");
        service.Repositories.ShouldBe(
            ["afonsoft/skills", "afonsoft/agent-harness", "octokit/octokit.net"]);
    }

    [Fact]
    public async Task Dado_ValorPersistidoValido_Quando_EnsureLoaded_Entao_UsaPersistido()
    {
        var (service, _, _) = Create(persisted: "octokit/octokit.net");
        await service.EnsureLoadedAsync();
        service.Selected.ShouldBe("octokit/octokit.net");
    }

    [Fact]
    public async Task Dado_PersistidoForaDaLista_Quando_EnsureLoaded_Entao_MantemPersistido()
    {
        // Free-text parity: a persisted owner/repo not in the list still wins.
        var (service, _, _) = Create(persisted: "me/private-repo");
        await service.EnsureLoadedAsync();
        service.Selected.ShouldBe("me/private-repo");
    }

    [Theory]
    [InlineData("just-a-name")]
    [InlineData("a/b/c")]
    [InlineData("")]
    public async Task Dado_PersistidoInvalido_Quando_EnsureLoaded_Entao_CaiNoPrimeiro(string persisted)
    {
        var (service, _, _) = Create(persisted: persisted);
        await service.EnsureLoadedAsync();
        service.Selected.ShouldBe("afonsoft/skills");
    }

    [Fact]
    public async Task Dado_Carregado_Quando_SelectValido_Entao_AtualizaPersisteEDisparaChanged()
    {
        var (service, _, js) = Create();
        await service.EnsureLoadedAsync();
        var fired = 0;
        service.Changed += () => { fired++; return Task.CompletedTask; };

        (await service.SelectAsync("octokit/octokit.net")).ShouldBeTrue();

        service.Selected.ShouldBe("octokit/octokit.net");
        fired.ShouldBe(1);
        await js.Received(1).InvokeAsync<string?>(
            "taskboard.setSelectedRepo",
            Arg.Is<object?[]?>(a => a != null && a.Contains("octokit/octokit.net")));
    }

    [Fact]
    public async Task Dado_MesmoValor_Quando_Select_Entao_NaoDisparaChanged()
    {
        var (service, _, _) = Create(persisted: "afonsoft/skills");
        await service.EnsureLoadedAsync();
        var fired = false;
        service.Changed += () => { fired = true; return Task.CompletedTask; };

        (await service.SelectAsync("afonsoft/skills")).ShouldBeTrue();

        fired.ShouldBeFalse();
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("a/b/c")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Dado_ValorInvalido_Quando_Select_Entao_RetornaFalseSemMudar(string? value)
    {
        var (service, _, _) = Create();
        await service.EnsureLoadedAsync();
        var before = service.Selected;

        (await service.SelectAsync(value)).ShouldBeFalse();

        service.Selected.ShouldBe(before);
    }

    [Fact]
    public async Task Dado_TokenAusente_Quando_EnsureLoaded_Entao_TokenMissingEWarning()
    {
        var (service, gitHub, _) = Create();
        gitHub.GetRepositoriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<RepositoryDto>>(
                new InvalidOperationException("no token")));

        await service.EnsureLoadedAsync();

        service.TokenMissing.ShouldBeTrue();
        service.Warning.ShouldNotBeNullOrEmpty();
        service.Repositories.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_FalhaGenerica_Quando_EnsureLoaded_Entao_WarningESelectFunciona()
    {
        var (service, gitHub, _) = Create();
        gitHub.GetRepositoriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<RepositoryDto>>(
                new HttpRequestException("boom")));

        await service.EnsureLoadedAsync();

        service.TokenMissing.ShouldBeFalse();
        service.Warning.ShouldNotBeNullOrEmpty();
        (await service.SelectAsync("me/private-repo")).ShouldBeTrue();
        service.Selected.ShouldBe("me/private-repo");
    }

    [Fact]
    public async Task Dado_StorageIndisponivel_Quando_LoadESelect_Entao_SemExcecao()
    {
        var (service, _, js) = Create();
        js.InvokeAsync<string?>(Arg.Any<string>(), Arg.Any<object?[]?>())
            .Returns(new ValueTask<string?>(
                Task.FromException<string?>(new JSException("storage blocked"))));

        await service.EnsureLoadedAsync();
        service.Selected.ShouldBe("afonsoft/skills");
        (await service.SelectAsync("octokit/octokit.net")).ShouldBeTrue();
        service.Selected.ShouldBe("octokit/octokit.net");
    }

    [Fact]
    public async Task Dado_EnsureLoaded2x_Quando_Concorrente_Entao_BuscaUmaVez()
    {
        var (service, gitHub, _) = Create();

        await Task.WhenAll(service.EnsureLoadedAsync(), service.EnsureLoadedAsync());

        await gitHub.Received(1).GetRepositoriesAsync(Arg.Any<CancellationToken>());
    }
}
