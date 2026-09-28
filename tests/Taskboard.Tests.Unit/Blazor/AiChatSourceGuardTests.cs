using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20260928-ai-code-ux-simplify: tripwire de estrutura do AiChat.razor —
/// o rail persistente substitui o modal "Threads", o composer mantém apenas
/// ações contextuais e a thread `terminal` renderiza o PtyThreadPane.
/// </summary>
public class AiChatSourceGuardTests
{
    private static string AiChatRazorPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return Path.Join(dir.FullName, "src", "Taskboard.Blazor", "Components", "Pages", "AiChat.razor");
    }

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_RailPersistentePresente()
    {
        // Covers RF-001 — ThreadRail montado dentro do .ai-chat-layout.
        var source = File.ReadAllText(AiChatRazorPath());

        source.ShouldContain("ai-chat-layout");
        source.ShouldContain("<ThreadRail");
        source.ShouldContain("OnToggleCollapsed");
    }

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_ModalDeThreadsRemovido()
    {
        // Covers RF-001 — o histórico não vive mais em modal.
        var source = File.ReadAllText(AiChatRazorPath());

        source.ShouldNotContain("_historyModal");
        source.ShouldNotContain("SelectThreadFromHistoryAsync");
    }

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_PtyPanePorKind()
    {
        // Covers RF-005 — threads Kind=="terminal" renderizam o slot PTY.
        var source = File.ReadAllText(AiChatRazorPath());

        source.ShouldContain("Kind == \"terminal\"");
        source.ShouldContain("<PtyThreadPane");
    }

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_RunAgentNoOverflow()
    {
        // Covers RF-004 — "Run agent" saiu do composer para o menu ⋯.
        var source = File.ReadAllText(AiChatRazorPath());

        source.ShouldContain("ai-chat-overflow-menu");
        source.ShouldContain("ShowRunAgentFromOverflowAsync");
    }
}
