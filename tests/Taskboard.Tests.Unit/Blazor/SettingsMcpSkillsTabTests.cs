using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20261010-mcp-skills-hub RF-006: a página Settings deve ter a aba
/// "MCP/Skills" (entre Chat e Security) com as três seções — AI Code MCP
/// tools, Agent CLI MCPs e Skills — e modais com fullscreen mobile. Contrato
/// por teste de fonte (padrão NavMenuOrderTests).
/// </summary>
public class SettingsMcpSkillsTabTests
{
    private static string SettingsSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        var path = Path.Join(dir.FullName, "src", "Taskboard.Blazor", "Components", "Pages", "Settings.razor");
        File.Exists(path).ShouldBeTrue($"Settings.razor não encontrado: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Dado_Settings_Quando_LeFonte_Entao_AbaMcpSkillsEntreChatESecurity()
    {
        var src = SettingsSource();

        var chat = src.IndexOf("(\"chat\", \"Chat\")", StringComparison.Ordinal);
        var mcpSkills = src.IndexOf("(\"mcp-skills\", \"MCP/Skills\")", StringComparison.Ordinal);
        var security = src.IndexOf("(\"security\", \"Security\")", StringComparison.Ordinal);

        chat.ShouldBeGreaterThan(0);
        mcpSkills.ShouldBeGreaterThan(chat, "aba MCP/Skills deve ficar logo depois de Chat");
        security.ShouldBeGreaterThan(mcpSkills, "aba Security deve vir depois de MCP/Skills");
    }

    [Fact]
    public void Dado_Settings_Quando_LeFonte_Entao_PaneMcpSkillsComTresSecoes()
    {
        var src = SettingsSource();

        src.ShouldContain("""aria-label="MCP/Skills" hidden="@(_activeTab != "mcp-skills")""");
        src.ShouldContain("form-section-title\">AI Code MCP tools");
        src.ShouldContain("form-section-title mb-0\">Agent CLI MCPs");
        src.ShouldContain("form-section-title\">Skills");
    }

    [Fact]
    public void Dado_Settings_Quando_LeFonte_Entao_PaneUsaEndpointsDoHub()
    {
        var src = SettingsSource();

        src.ShouldContain("GetChatMcpServersAsync");   // GET /api/mcp/chat
        src.ShouldContain("GetAgentMcpInventoryAsync"); // GET /api/mcp/agents
        src.ShouldContain("InstallAgentMcpAsync");      // POST /api/mcp/agents/install
        src.ShouldContain("RemoveAgentMcpAsync");       // POST /api/mcp/agents/remove
        src.ShouldContain("SearchSkillsAsync");         // GET /api/skills/search
        src.ShouldContain("InstallSkillsRepositoryAsync"); // POST /api/skills/install-repo
        src.ShouldContain("InstallSkillAsync");         // POST /api/skills/install-one
    }

    [Fact]
    public void Dado_Settings_Quando_LeFonte_Entao_ToggleIncludeGlobalAgents()
    {
        var src = SettingsSource();

        src.ShouldContain("Taskboard:Chat:Mcp:IncludeGlobalAgents");
        src.ShouldContain("Taskboard:Chat:Mcp:Enabled");
    }

    [Fact]
    public void Dado_Settings_Quando_LeFonte_Entao_ModaisFullscreenMobile()
    {
        var src = SettingsSource();

        var add = src.IndexOf("_mcpAddModal", StringComparison.Ordinal);
        add.ShouldBeGreaterThan(0);
        var addModal = src.IndexOf("<Modal @ref=\"_mcpAddModal\"", StringComparison.Ordinal);
        var addFullscreen = src.IndexOf("Fullscreen=\"ModalFullscreen.SmallDown\"", addModal);
        addFullscreen.ShouldBeGreaterThan(0, "modal Add MCP deve ser fullscreen em telas pequenas");

        var removeModal = src.IndexOf("<Modal @ref=\"_mcpRemoveModal\"", StringComparison.Ordinal);
        var removeFullscreen = src.IndexOf("Fullscreen=\"ModalFullscreen.SmallDown\"", removeModal);
        removeFullscreen.ShouldBeGreaterThan(0, "modal Remove MCP deve ser fullscreen em telas pequenas");
    }

    [Fact]
    public void Dado_Settings_Quando_LeFonte_Entao_KeysUnicasEmListasComDupNames()
    {
        // O mesmo skill/MCP name aparece em várias fontes (agents/claude/…)
        // e até duplicado na mesma fonte — @key por Name quebra o render
        // ("same key value"), matando o circuito Blazor inteiro (inclusive
        // a aba Configuration, que renderiza os outros panes no DOM).
        var src = SettingsSource();

        src.ShouldContain("@key=\"skill.Path\"");
        src.ShouldNotContain("@key=\"skill.Name\"");
        src.ShouldContain("@key=\"ChatMcpServerKey(server)\"");
        src.ShouldContain("@key=\"AgentMcpServerKey(server)\"");
        src.ShouldContain("server.Name + \"@\" + server.Origin");
        src.ShouldContain("server.Name + \"@\" + server.Transport + \"@\" + server.Detail");
    }

    [Fact]
    public void Dado_Settings_Quando_LeFonte_Entao_SecaoCacheNaAbaConfiguration()
    {
        // A aba Configuration mostra o provider (memory|redis) e a lista de
        // keys salvas — bloco 'cache' do /api/configuration populando
        // _cacheStats. A key inclui Source porque a mesma key pode vir do
        // registry e do SCAN do redis ao mesmo tempo.
        var src = SettingsSource();

        src.ShouldContain("_cacheStats");
        src.ShouldContain("_cacheStats.RedisConfigured");
        src.ShouldContain("_cacheStats.RedisConnected");
        src.ShouldContain("@key=\"CacheKeyRowKey(key)\"");
        src.ShouldContain("item.Source + \"@\" + item.Key");
        src.ShouldContain("_cacheStats = snapshot?.Cache;");
    }
}
