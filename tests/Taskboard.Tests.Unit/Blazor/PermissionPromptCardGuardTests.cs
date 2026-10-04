using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20261004-permission-question-cards RF-003: tripwire do
/// PermissionPromptCard — cada opção deve responder com o optionId literal
/// (round-trip correto para perguntas arbitrárias tipo AskUserQuestion) e o
/// card deve distinguir permission request de agent question.
/// </summary>
public sealed class PermissionPromptCardGuardTests
{
    private static string CardSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return File.ReadAllText(Path.Join(
            dir.FullName, "src", "Taskboard.Blazor", "Components", "AiChat", "PermissionPromptCard.razor"));
    }

    [Fact]
    public void Dado_PermissionPromptCard_Quando_LeFonte_Entao_ReplyComOptionId()
    {
        var source = CardSource();

        source.ShouldContain("EffectiveOptions");
        source.ShouldContain("opt.OptionId");
    }

    [Fact]
    public void Dado_PermissionPromptCard_Quando_LeFonte_Entao_DistinguePerguntaDePermissao()
    {
        var source = CardSource();

        source.ShouldContain("chat.permissionRequest");
        source.ShouldContain("chat.agentQuestion");
    }
}
