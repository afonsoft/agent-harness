using Shouldly;
using Taskboard.CliMetrics;
using Xunit;

namespace Taskboard.Tests.Unit.CliMetrics;

/// <summary>SPEC-20260920-harness-recurring-jobs RF-002 — normalização de ModelName.</summary>
public class CliModelNameTests
{
    [Fact]
    public void Dado_EnvelopeJson_Quando_Normalize_Entao_ExtraiProviderEId()
    {
        var (provider, model) = CliModelName.Normalize(
            """{"id":"Opus","providerID":"omniroute","variant":"default"}""");

        provider.ShouldBe("omniroute");
        model.ShouldBe("Opus");
    }

    [Fact]
    public void Dado_NomePlano_Quando_Normalize_Entao_ModeloEhOProprioNome()
    {
        var (provider, model) = CliModelName.Normalize("claude-3-7-sonnet");

        provider.ShouldBeNull();
        model.ShouldBe("claude-3-7-sonnet");
    }

    [Fact]
    public void Dado_JsonInvalido_Quando_Normalize_Entao_FallbackParaStringBruta()
    {
        var (provider, model) = CliModelName.Normalize("{not-json");

        provider.ShouldBeNull();
        model.ShouldBe("{not-json");
    }

    [Fact]
    public void Dado_NuloOuVazio_Quando_Normalize_Entao_Nulos()
    {
        CliModelName.Normalize(null).ShouldBe((null, null));
        CliModelName.Normalize("").ShouldBe((null, null));
        CliModelName.Normalize("   ").ShouldBe((null, null));
    }

    [Fact]
    public void Dado_JsonSemId_Quando_Normalize_Entao_ModeloNulo()
    {
        var (provider, model) = CliModelName.Normalize("""{"providerID":"omniroute"}""");

        provider.ShouldBe("omniroute");
        model.ShouldBeNull();
    }
}
