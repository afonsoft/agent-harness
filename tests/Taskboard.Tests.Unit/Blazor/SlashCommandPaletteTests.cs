using Bunit;
using Shouldly;
using Taskboard.Blazor.Components.Chat;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// Slash palette: exact matches e builtins devem vir antes de skills para que
/// Enter no "/help" execute o builtin em vez de selecionar a primeira skill.
/// </summary>
public class SlashCommandPaletteTests : BunitContext
{
    private static IReadOnlyList<SlashCommandPalette.SlashItem> Items() =>
    [
        new("caveman-help", "skill help", "skill"),
        new("abp-angular", "skill angular", "skill"),
        new("help", "Listar comandos", "builtin"),
        new("new", "Nova conversa", "builtin"),
    ];

    [Fact]
    public void Dado_PaletteAberta_Quando_Renderiza_Entao_ListaItems()
    {
        var cut = Render<SlashCommandPalette>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Filter, "he")
            .Add(x => x.Items, Items()));

        cut.FindAll(".slash-item").Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Dado_FiltroExato_Quando_AplicaFiltro_Entao_MatchExatoPrimeiro()
    {
        var filtered = SlashCommandPalette.ApplyFilter(Items(), "help");

        filtered[0].Command.ShouldBe("help");
        filtered[0].Kind.ShouldBe("builtin");
    }

    [Fact]
    public void Dado_FiltroParcial_Quando_AplicaFiltro_Entao_BuiltinsAntesDeSkills()
    {
        var filtered = SlashCommandPalette.ApplyFilter(Items(), "e");

        filtered.TakeWhile(i => i.Kind == "builtin").Count().ShouldBe(2);
        filtered.Skip(2).ShouldAllBe(i => i.Kind == "skill");
    }
}
