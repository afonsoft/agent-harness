using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20261010-settings-configuration-tab: tripwire de estrutura do
/// Settings.razor — aba "configuration" dedicada entre General e Integrations,
/// General só com Features, seções por grupo, info header de Connections e
/// bloco "Managed in other screens" com as chaves dedupadas.
/// </summary>
public class SettingsConfigurationTabTests
{
    private static string SettingsRazorPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return Path.Join(dir.FullName, "src", "Taskboard.Blazor", "Components", "Pages", "Settings.razor");
    }

    private static string Source() => File.ReadAllText(SettingsRazorPath());

    [Fact]
    public void Dado_SettingsTabs_Quando_LeFonte_Entao_ConfigurationDepoisDeGeneral()
    {
        var source = Source();

        var general = source.IndexOf("(\"general\", \"General\")", StringComparison.Ordinal);
        var configuration = source.IndexOf("(\"configuration\", \"Configuration\")", StringComparison.Ordinal);
        var integrations = source.IndexOf("(\"integrations\", \"Integrations\")", StringComparison.Ordinal);

        general.ShouldBeGreaterThanOrEqualTo(0);
        configuration.ShouldBeGreaterThan(general);
        integrations.ShouldBeGreaterThan(configuration);
    }

    [Fact]
    public void Dado_SettingsRazor_Quando_LeFonte_Entao_PaneConfigurationComHidden()
    {
        var source = Source();

        source.ShouldContain("aria-label=\"Configuration\"");
        source.ShouldContain("""hidden="@(_activeTab != "configuration")" """.TrimEnd());
    }

    [Fact]
    public void Dado_GeneralPane_Quando_LeFonte_Entao_SomenteFeatures()
    {
        // A seção "Configuration" saiu do General — o título agora vive só na
        // aba dedicada (uma única ocorrência: a info header de Connections).
        var source = Source();
        var generalPane = source.IndexOf("aria-label=\"General\"", StringComparison.Ordinal);
        var configPane = source.IndexOf("aria-label=\"Configuration\"", StringComparison.Ordinal);
        generalPane.ShouldBeGreaterThanOrEqualTo(0);
        configPane.ShouldBeGreaterThan(generalPane);

        var generalSection = source[generalPane..configPane];
        generalSection.ShouldContain("Features");
        generalSection.ShouldNotContain("_configEntries");
        generalSection.ShouldNotContain("form-section-title\">Configuration");
    }

    [Fact]
    public void Dado_ConfigPane_Quando_LeFonte_Entao_SecoesPorGrupo()
    {
        var source = Source();
        var paneStart = source.IndexOf("aria-label=\"Configuration\"", StringComparison.Ordinal);
        paneStart.ShouldBeGreaterThanOrEqualTo(0);
        var pane = source[paneStart..];

        pane.ShouldContain("ConfigurationGroupSections");
        pane.ShouldContain("e.ManagedIn is null && e.Group == groupId");
        // cinco grupos na ordem do spec
        pane.ShouldContain("ConfigurationGroups.Connections");
        pane.ShouldContain("ConfigurationGroups.Server");
        pane.ShouldContain("ConfigurationGroups.Logging");
        pane.ShouldContain("ConfigurationGroups.Chat");
        pane.ShouldContain("ConfigurationGroups.Security");
    }

    [Fact]
    public void Dado_ConfigPane_Quando_LeFonte_Entao_ConnectionsInfoHeader()
    {
        var source = Source();

        source.ShouldContain("_connectionInfo.DbProvider");
        source.ShouldContain("_connectionInfo.CacheMode");
        source.ShouldContain("PostgreSQL");
        source.ShouldContain("SQLite");
        source.ShouldContain("Redis (L1+L2)");
        source.ShouldContain("In-memory (L1)");
        source.ShouldContain("_connectionInfo.DbConnectionString");
    }

    [Fact]
    public void Dado_ConfigPane_Quando_LeFonte_Entao_ManagedInLinks()
    {
        var source = Source();
        var paneStart = source.IndexOf("aria-label=\"Configuration\"", StringComparison.Ordinal);
        var pane = source[paneStart..];

        pane.ShouldContain("Managed in other screens");
        pane.ShouldContain("e.ManagedIn is not null");
        pane.ShouldContain("href=\"@entry.ManagedIn\"");
    }

    [Fact]
    public void Dado_ConfigSnapshot_Quando_LeFonte_Entao_LoadUsaGetConfigurationSnapshot()
    {
        var source = Source();

        source.ShouldContain("GetConfigurationSnapshotAsync");
        source.ShouldContain("snapshot?.Connections");
    }
}
