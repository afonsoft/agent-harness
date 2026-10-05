using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20260929-webcli-toggle-finops-active-sessions RF-002: a página
/// Settings deve ter uma seção "Features" com switches dedicados para as
/// chaves booleanas do catálogo (Web CLI Agent, Terminal) dentro da aba
/// General — a tabela Configuration virou uma aba dedicada
/// (SPEC-20261010-settings-configuration-tab, coberta por
/// SettingsConfigurationTabTests). A presença é contrato — regressão coberta
/// por teste de fonte (padrão NavMenuOrderTests).
/// </summary>
public class SettingsFeaturesTests
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
    public void Dado_Settings_Quando_LeFonte_Entao_SecaoFeaturesNaAbaGeneral()
    {
        var src = SettingsSource();

        var generalPane = src.IndexOf("aria-label=\"General\"", StringComparison.Ordinal);
        var features = src.IndexOf("form-section-title\">Features", StringComparison.Ordinal);
        var configPane = src.IndexOf("aria-label=\"Configuration\"", StringComparison.Ordinal);

        generalPane.ShouldBeGreaterThan(0, "aba General deve existir no Settings");
        features.ShouldBeGreaterThan(generalPane, "Features deve estar na aba General");
        configPane.ShouldBeGreaterThan(features, "a aba Configuration deve vir depois da seção Features");
    }

    [Fact]
    public void Dado_Settings_Quando_LeFonte_Entao_WebCliAgentETerminalComSwitch()
    {
        var src = SettingsSource();

        src.Contains("Taskboard:WebCliAgent:Enabled", StringComparison.Ordinal)
            .ShouldBeTrue("Web CLI Agent deve ter toggle dedicado");
        src.Contains("Taskboard:Terminal:Enabled", StringComparison.Ordinal)
            .ShouldBeTrue("Terminal deve ter toggle dedicado");
        src.Contains("ToggleFeatureAsync", StringComparison.Ordinal)
            .ShouldBeTrue("o switch deve persistir via override no banco");
        src.Contains("role=\"switch\"", StringComparison.Ordinal)
            .ShouldBeTrue("toggles devem renderizar como switches");
    }

    [Fact]
    public void Dado_Settings_Quando_LeFonte_Entao_SemLiteralEmVariavelDeCredencial()
    {
        // Covers SPEC-20260930-sonar-s2068 RF-001: nenhum membro com nome de
        // credencial (password/passwd/pwd) pode receber literal de string —
        // é o padrão que o SonarQube S2068 marca como hard-coded credential.
        var src = SettingsSource();

        System.Text.RegularExpressions.Regex.IsMatch(
            src, @"(?i)\b\w*(password|passwd|pwd)\w*\s*=\s*""")
            .ShouldBeFalse("literal atribuído a membro com nome de credencial dispara S2068");
    }
}
