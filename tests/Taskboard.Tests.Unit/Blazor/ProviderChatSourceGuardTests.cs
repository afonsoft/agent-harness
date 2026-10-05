using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20261005-chat-background-resume RF-006/RF-007: tripwire de estrutura
/// do ProviderChat/AiChat — send via enqueue+attach (não mais stream direto),
/// filtro Ativas|Arquivadas, rail slim no collapsed, ☰ duplicado removido do
/// chat header e a seção "Legacy threads" dentro do drawer.
/// </summary>
public class ProviderChatSourceGuardTests
{
    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return Path.Join(new[] { dir.FullName }.Concat(parts).ToArray());
    }

    private static string ProviderChatRazor() =>
        File.ReadAllText(RepoPath("src", "Taskboard.Blazor", "Components", "Chat", "ProviderChat.razor"));

    private static string ProviderChatCss() =>
        File.ReadAllText(RepoPath("src", "Taskboard.Blazor", "Components", "Chat", "ProviderChat.razor.css"));

    private static string AiChatRazor() =>
        File.ReadAllText(RepoPath("src", "Taskboard.Blazor", "Components", "Pages", "AiChat.razor"));

    private static string TaskboardClient() =>
        File.ReadAllText(RepoPath("src", "Taskboard.Blazor", "Services", "TaskboardClient.cs"));

    [Fact]
    public void Dado_ProviderChat_Quando_LeFonte_Entao_SendUsaEnqueueEAttach()
    {
        // RF-002/RF-003: o POST só enfileira; o progresso vem do attach stream.
        var source = ProviderChatRazor();

        source.ShouldContain("EnqueueChatMessageAsync");
        source.ShouldContain("OpenChatRunStreamAsync");
        source.ShouldContain("chat.sync");
        source.ShouldNotContain("SendChatMessageAsync(");
        source.ShouldNotContain("StreamAssistantReplyAsync");
    }

    [Fact]
    public void Dado_ProviderChat_Quando_LeFonte_Entao_ArchiveUxPresente()
    {
        // RF-006: filtro Ativas|Arquivadas, ações archive/restore/delete e
        // composer read-only em conversa arquivada.
        var source = ProviderChatRazor();

        source.ShouldContain("SetArchivedFilterAsync");
        source.ShouldContain("ArchiveConversationAsync");
        source.ShouldContain("UnarchiveConversationAsync");
        source.ShouldContain("Arquivadas");
        source.ShouldContain("Conversa arquivada");
        source.ShouldContain("SetChatConversationArchivedAsync");
    }

    [Fact]
    public void Dado_ProviderChat_Quando_LeFonte_Entao_SemBotaoHistoricoNoHeader()
    {
        // RF-007: o header do chat não tem mais um segundo ☰ — a sidebar é a
        // única superfície de histórico (Histórico vive nela e na rail).
        var source = ProviderChatRazor();
        var headerStart = source.IndexOf("provider-chat-header", StringComparison.Ordinal);
        headerStart.ShouldBeGreaterThan(-1);
        var headerEnd = source.IndexOf("</div>", headerStart, StringComparison.Ordinal);
        var header = source[headerStart..headerEnd];

        header.ShouldNotContain("ToggleSidebar");
        header.ShouldNotContain("Histórico");
    }

    [Fact]
    public void Dado_ProviderChatCss_Quando_LeFonte_Entao_CollapsedOculto()
    {
        // Sidebar collapsed fica totalmente oculta — só abre pelo botão de
        // histórico (clock-history) do header da página.
        var css = ProviderChatCss();

        css.ShouldContain(".provider-chat-sidebar.collapsed");
        css.ShouldContain("display: none;");
        css.ShouldContain("provider-chat-run-dot");
        css.ShouldNotContain("provider-chat-rail-item");
    }

    [Fact]
    public void Dado_AiChat_Quando_LeFonte_Entao_HeaderProxyaParaProviderChat()
    {
        var source = AiChatRazor();

        source.ShouldContain("_providerChat");
        source.ShouldContain("ToggleHistoryDrawer");
        source.ShouldContain("StartNewChatPublicAsync");
        source.ShouldContain("HistoryExtra");
        source.ShouldContain("Legacy threads");
    }

    [Fact]
    public void Dado_TaskboardClient_Quando_LeFonte_Entao_MetodosDeRun()
    {
        var client = TaskboardClient();

        client.ShouldContain("EnqueueChatMessageAsync");
        client.ShouldContain("OpenChatRunStreamAsync");
        client.ShouldContain("SetChatConversationArchivedAsync");
        client.ShouldContain("archived=");
    }
}
