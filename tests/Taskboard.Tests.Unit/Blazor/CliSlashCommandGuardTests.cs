using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20261004-cli-slash-commands RF-004: tripwire da palette de CLI —
/// o composer do AiChat deve carregar os comandos do CLI selecionado e a
/// palette deve distinguir itens "cli" com badge.
/// </summary>
public sealed class CliSlashCommandGuardTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return File.ReadAllText(Path.Join([dir.FullName, .. parts]));
    }

    [Fact]
    public void Dado_AiChat_Quando_LeFonte_Entao_CarregaComandosDaCli()
    {
        var source = RepoFile("src", "Taskboard.Blazor", "Components", "Pages", "AiChat.razor");

        source.ShouldContain("GetCliCommandsAsync");
        source.ShouldContain("\"cli\"");
    }

    [Fact]
    public void Dado_SlashPalette_Quando_LeFonte_Entao_RenderizaBadgeCli()
    {
        var source = RepoFile("src", "Taskboard.Blazor", "Components", "Chat", "SlashCommandPalette.razor");

        source.ShouldContain("cli");
        source.ShouldContain("Origin");
    }
}
