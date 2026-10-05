using System.Text.RegularExpressions;
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

    private static string ProviderChatRazorPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return Path.Join(dir.FullName, "src", "Taskboard.Blazor", "Components", "Chat", "ProviderChat.razor");
    }

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_RailOverlayPresente()
    {
        // SPEC-20260929-ai-chat-rail-overlay RF-001 — ThreadRail dentro do
        // overlay (não mais rail persistente com toggle de colapso).
        var source = File.ReadAllText(AiChatRazorPath());

        source.ShouldContain("ai-chat-layout");
        source.ShouldContain("<ThreadRail");
        source.ShouldContain("ai-chat-rail-wrap");
        source.ShouldNotContain("OnToggleCollapsed");
        source.ShouldNotContain("_railCollapsed");
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

    // SPEC-20260929-ai-chat-rail-overlay.

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_BotoesHistoryENewNoTopoDireito()
    {
        // RF-002 — History + New no topo-direito sobre o painel de chat.
        var source = File.ReadAllText(AiChatRazorPath());

        var msAuto = source.IndexOf("ms-auto", StringComparison.Ordinal);
        var history = source.IndexOf("OpenRailDrawerAsync", msAuto, StringComparison.Ordinal);
        var newChat = source.IndexOf("NewConversationAsync", msAuto, StringComparison.Ordinal);

        msAuto.ShouldBeGreaterThanOrEqualTo(0, "grupo ms-auto com os botões não encontrado");
        history.ShouldBeGreaterThan(msAuto, "botão History deve estar no grupo ms-auto");
        newChat.ShouldBeGreaterThan(msAuto, "botão New deve estar no grupo ms-auto");
    }

    [Fact]
    public void Dado_SiteCss_Quando_LeFonte_Entao_RailWrapNaoEhFlexItem()
    {
        // RF-001 — .ai-chat-rail-wrap é overlay fixed, não flex item do layout.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        var css = File.ReadAllText(
            Path.Join(dir!.FullName, "src", "Taskboard.Client", "wwwroot", "css", "site.css"));

        var wrap = css.IndexOf(".ai-chat-rail-wrap {", StringComparison.Ordinal);
        wrap.ShouldBeGreaterThanOrEqualTo(0);
        css[wrap..(wrap + 60)].ShouldContain("display: none");

        var open = css.IndexOf(".ai-chat-rail-wrap.drawer-open {", StringComparison.Ordinal);
        open.ShouldBeGreaterThanOrEqualTo(0);
        css[open..(open + 200)].ShouldContain("position: fixed");
    }

    // SPEC-20260929-ai-chat-view-first.

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_ViewAntesDoAgentCli()
    {
        // RF-001 — o select View precede o select Agent CLI na command bar.
        var source = File.ReadAllText(AiChatRazorPath());

        var view = source.IndexOf("OnCfgViewChanged", StringComparison.Ordinal);
        var cli = source.IndexOf("OnCfgCliChanged", StringComparison.Ordinal);
        view.ShouldBeGreaterThanOrEqualTo(0);
        cli.ShouldBeGreaterThan(view, "o select View (OnCfgViewChanged) deve vir antes do Agent CLI (OnCfgCliChanged)");
    }

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_CliSemAcpDisabledEmChat()
    {
        // RF-002 — em View=chat, opções sem SupportsChat ficam disabled.
        var source = File.ReadAllText(AiChatRazorPath());

        source.ShouldContain("_cfgView == \"chat\" && !opt.SupportsChat");
        source.ShouldContain("AnyChatCapableCli");
    }

    // SPEC-20261001-pr-review-backlog-fixes.

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_DraftSobreviveAFalhaDeQueue()
    {
        // B-11 — o composer só é limpo depois do check `queued is null`; limpar
        // antes descartava o texto do usuário em qualquer erro de rede.
        var source = File.ReadAllText(AiChatRazorPath());

        var queueCall = source.IndexOf("QueueAgentThreadPromptAsync", StringComparison.Ordinal);
        var nullCheck = source.IndexOf("queued is null", queueCall, StringComparison.Ordinal);
        var clear = source.IndexOf("_composer = string.Empty;", queueCall, StringComparison.Ordinal);

        queueCall.ShouldBeGreaterThanOrEqualTo(0);
        nullCheck.ShouldBeGreaterThan(queueCall);
        clear.ShouldBeGreaterThan(nullCheck, "o composer deve ser limpo apenas após o sucesso do queue");
    }

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_EscFechaDrawer()
    {
        // B-12 — Esc dispensa o drawer mobile do rail (só havia o scrim tap).
        var source = File.ReadAllText(AiChatRazorPath());

        source.ShouldContain("OnPageKeyDown");
        source.ShouldContain("\"Escape\"");
        var handler = source.IndexOf("private void OnPageKeyDown", StringComparison.Ordinal);
        handler.ShouldBeGreaterThanOrEqualTo(0);
        source[handler..(handler + 400)].ShouldContain("_railDrawerOpen = false");
    }

    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_AutoSelectPersisteCli()
    {
        // B-14/#387 — a CLI auto-selecionada na inicialização também é
        // persistida (o fallback ao primeiro elegível não deve “desaprender” a
        // escolha entre sessões).
        var source = File.ReadAllText(AiChatRazorPath());

        var occurrences = Regex.Matches(source, "taskboard\\.setAiChatLastAgent").Count;
        occurrences.ShouldBeGreaterThanOrEqualTo(2,
            "setAiChatLastAgent deve ser chamado tanto no auto-select do init quanto em OnCfgCliChanged");
    }
    [Fact]
    public void Dado_AiChatRazor_Quando_LeFonte_Entao_ChatModeTemBotoesWorkspaceRepo()
    {
        // Os botões Workspace/Repositório do modo Agent existem também no
        // ProviderChat do modo Chat — mesmo mecanismo (AgentContext + PATCH).
        var source = File.ReadAllText(AiChatRazorPath());

        var chatBlock = source.IndexOf("_cfgMode == \"provider\"", StringComparison.Ordinal);
        var agentBlock = source.IndexOf("_activeThread is null", StringComparison.Ordinal);
        chatBlock.ShouldBeGreaterThanOrEqualTo(0);
        agentBlock.ShouldBeGreaterThan(chatBlock);

        var chatPane = source[chatBlock..agentBlock];
        chatPane.ShouldContain("AgentContext=\"CurrentAgentContext\"");
        chatPane.ShouldContain("OnConversationAgentLoaded=\"OnConversationAgentLoaded\"");
        chatPane.ShouldContain("ShowWorkspaceModalAsync");
        chatPane.ShouldContain("ShowRepoModalAsync");
    }

    [Fact]
    public void Dado_ProviderChatRazor_Quando_LeFonte_Entao_BadguaSchedule()
    {
        // SPEC-20261005-chat-jobs-schedule-search — mensagem entregue pelo
        // dispatcher (Kind="schedule") deve badjar como "schedule" (assim
        // como "steer"), não renderizar como bubble de user comum.
        var source = File.ReadAllText(ProviderChatRazorPath());

        source.ShouldContain("message.Kind == \"schedule\"");
        source.ShouldContain(">schedule</span>");
    }
}
