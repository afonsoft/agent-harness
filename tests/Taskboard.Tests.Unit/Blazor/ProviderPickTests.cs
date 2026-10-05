using System.Text.RegularExpressions;
using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20261004-provider-auto-select: o catálogo é alfabético, então
/// `providers[0]` nem sempre é utilizável — o default do composer deve
/// preferir um provider enabled com key, senão enabled, senão o primeiro.
/// </summary>
public sealed class ProviderPickTests
{
    private static ChatProviderDto Provider(string name, bool enabled, bool hasKey) =>
        new(Guid.NewGuid(), name, "https://api.test/v1", enabled, hasKey,
            hasKey ? "sk-…" : string.Empty, DateTime.UnixEpoch, DateTime.UnixEpoch);

    [Fact]
    public void Dado_ListaVazia_Quando_PreferAvailable_Entao_Null()
    {
        ChatProviderPick.PreferAvailable([]).ShouldBeNull();
    }

    [Fact]
    public void Dado_PrimeiroAlfabeticoDesabilitado_Quando_PreferAvailable_Entao_EscolheEnabledComKey()
    {
        var disabledWithKey = Provider("a-disabled", enabled: false, hasKey: true);
        var enabledWithKey = Provider("z-enabled", enabled: true, hasKey: true);

        ChatProviderPick.PreferAvailable([disabledWithKey, enabledWithKey])
            .ShouldBe(enabledWithKey);
    }

    [Fact]
    public void Dado_EnabledSemKeyEEnabledComKey_Quando_PreferAvailable_Entao_PrefereComKey()
    {
        var enabledNoKey = Provider("a-ollama", enabled: true, hasKey: false);
        var enabledWithKey = Provider("z-omniroute", enabled: true, hasKey: true);

        ChatProviderPick.PreferAvailable([enabledNoKey, enabledWithKey])
            .ShouldBe(enabledWithKey);
    }

    [Fact]
    public void Dado_SoEnabledSemKey_Quando_PreferAvailable_Entao_EscolheEnabled()
    {
        var disabled = Provider("a-disabled", enabled: false, hasKey: true);
        var enabledNoKey = Provider("z-ollama", enabled: true, hasKey: false);

        ChatProviderPick.PreferAvailable([disabled, enabledNoKey])
            .ShouldBe(enabledNoKey);
    }

    [Fact]
    public void Dado_NenhumEnabled_Quando_PreferAvailable_Entao_CaiNoPrimeiro()
    {
        var first = Provider("a-disabled", enabled: false, hasKey: false);
        var second = Provider("b-disabled", enabled: false, hasKey: true);

        ChatProviderPick.PreferAvailable([first, second]).ShouldBe(first);
    }

    [Fact]
    public void Dado_ProviderChat_Quando_LeFonte_Entao_UsaPreferAvailable()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        var source = File.ReadAllText(Path.Join(
            dir!.FullName, "src", "Taskboard.Blazor", "Components", "Chat", "ProviderChat.razor"));

        source.ShouldContain("ChatProviderPick.PreferAvailable");
        source.ShouldNotContain("_providerId = _providers[0].Id");
    }

    [Fact]
    public void Dado_ProviderChat_Quando_LeFonte_Entao_PillUnicoComDropdownDoisEstagios()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        var root = dir!.FullName;
        var chat = File.ReadAllText(Path.Join(
            root, "src", "Taskboard.Blazor", "Components", "Chat", "ProviderChat.razor"));
        var page = File.ReadAllText(Path.Join(
            root, "src", "Taskboard.Blazor", "Components", "Pages", "AiChat.razor"));

        // Pill único: ícone + texto do modelo; sem selects de provider/modelo.
        chat.ShouldContain("provider-pick-btn");
        chat.ShouldContain("provider-pick-panel");
        chat.ShouldNotContain("provider-pick-select");
        // Estágio de providers pulado quando só há um cadastrado.
        chat.ShouldContain("_providers.Count > 1");
        // Slot da agent bar dentro do composer footer.
        chat.ShouldContain("public RenderFragment? AgentTools");
        page.ShouldContain("<AgentTools>");
        // A barra da Agent-mode desceu pro fragmento — sobra só a toolbar
        // do composer legado (thread ativa).
        Regex.Matches(page, "ai-chat-toolbar small").Count.ShouldBe(1);
    }
}
