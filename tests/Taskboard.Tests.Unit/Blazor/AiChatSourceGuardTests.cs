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

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_PtyPaneComKey()
    {
        // SPEC-20260929-pty-session-security RF-003 — sem @key o Blazor reutiliza
        // a instância ao trocar de thread, empilhando handlers e xterms órfãos.
        var source = File.ReadAllText(AiChatRazorPath());

        source.ShouldContain("<PtyThreadPane");
        source.ShouldContain("@key");
    }

    [Fact]
    public void Dado_PtyPane_Quando_LeFonte_Entao_SessionIdPreSetadoParaReplay()
    {
        // SPEC-20260929-pty-session-security RF-004 — o id determinístico é
        // atribuído antes do invoke para o replay de scrollback passar no filtro.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        var pane = File.ReadAllText(Path.Join(
            dir!.FullName, "src", "Taskboard.Blazor", "Components", "AiChat", "PtyThreadPane.razor"));

        var assign = pane.IndexOf("_sessionId = $\"t-{Thread.Id}\"", StringComparison.Ordinal);
        var invoke = pane.IndexOf("InvokeAsync<string>(\"OpenForThread\"", StringComparison.Ordinal);
        assign.ShouldBeGreaterThanOrEqualTo(0);
        invoke.ShouldBeGreaterThan(assign, "o sessionId determinístico deve ser atribuído antes do OpenForThread");
    }

    // SPEC-20260929-ai-code-ux-fixes.

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_NewCriaThread()
    {
        // RF-001 — New materializa a thread via API, não apenas limpa o estado.
        var source = File.ReadAllText(AiChatRazorPath());

        var newBody = source.IndexOf("NewConversationAsync", StringComparison.Ordinal);
        var create = source.IndexOf("CreateAiChatThreadAsync", newBody, StringComparison.Ordinal);
        create.ShouldBeGreaterThan(newBody, "NewConversationAsync deve chamar CreateAiChatThreadAsync");
    }

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_NewFechaDrawerEUsoRepoGlobal()
    {
        // RF-002/RF-003 — New fecha o drawer mobile e prefere o seletor global.
        var source = File.ReadAllText(AiChatRazorPath());

        var newBody = source.IndexOf("private async Task NewConversationAsync", StringComparison.Ordinal);
        var end = source.IndexOf("\n    }", newBody, StringComparison.Ordinal);
        var body = source[newBody..end];

        body.ShouldContain("_railDrawerOpen = false");
        body.ShouldContain("SelectedRepo.Selected");
    }

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_AcoesCondicionadasAoEstado()
    {
        // RF-004/RF-005 — drawer recarrega threads; Run agent bloqueia em voo;
        // Retry exige prompt do usuário.
        var source = File.ReadAllText(AiChatRazorPath());

        source.ShouldContain("OpenRailDrawerAsync");
        source.ShouldContain("RefreshThreadsAsync");
        source.ShouldContain("!HasUserPrompt");
    }
}
