using System.Text.Json;
using Shouldly;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.AiChat;

/// <summary>
/// SPEC-20260921-ai-code-thread-config RF-005(a): os configOptions de
/// categoria "model" da sessão ACP viram entradas de catálogo "acp".
/// </summary>
public class AcpSessionModelCatalogTests
{
    [Fact]
    public void Dado_ConfigOptionsComCategoriaModel_Quando_Parse_Entao_ModelosAcp()
    {
        var opts = ParseOptions("""
        [
            { "id": "model", "name": "Model", "category": "model", "value": "gpt-5",
              "options": [{ "value": "gpt-5", "name": "GPT-5" }, { "value": "gpt-5-mini" }] },
            { "id": "verbose", "category": "other", "type": "boolean", "value": false }
        ]
        """);

        var models = AcpSessionModelCatalog.ParseModels(opts, "OpenCode");

        models.Count.ShouldBe(2);
        models.ShouldAllBe(m => m.Source == "acp" && m.AgentType == "OpenCode");
        models[0].Name.ShouldBe("gpt-5");
        models[1].Name.ShouldBe("gpt-5-mini");
    }

    [Fact]
    public void Dado_CategoriaModelSemOptions_Quando_Parse_Entao_ValorCorrenteViraEntrada()
    {
        var opts = ParseOptions("""
        [ { "id": "model", "category": "model", "value": "claude-sonnet" } ]
        """);

        var models = AcpSessionModelCatalog.ParseModels(opts, "Claude");

        models.Count.ShouldBe(1);
        models[0].Name.ShouldBe("claude-sonnet");
        models[0].Source.ShouldBe("acp");
    }

    [Fact]
    public void Dado_ConfigIdAlternativo_Quando_Parse_Entao_ReconheceModel()
    {
        // v2-readiness: peers que usam "configId" em vez de "id".
        var opts = ParseOptions("""
        [ { "configId": "model", "category": "model", "options": [{ "value": "m1" }] } ]
        """);

        var models = AcpSessionModelCatalog.ParseModels(opts, "OpenCode");

        models.Count.ShouldBe(1);
        models[0].Name.ShouldBe("m1");
    }

    [Fact]
    public void Dado_SemCategoriaModel_Quando_Parse_Entao_Vazio()
    {
        var opts = ParseOptions("""
        [ { "id": "reasoning", "category": "thought_level", "options": [{ "value": "high" }] } ]
        """);

        AcpSessionModelCatalog.ParseModels(opts, "OpenCode").ShouldBeEmpty();
    }

    [Fact]
    public void Dado_SemConfigOptions_Quando_Parse_Entao_Vazio()
    {
        AcpSessionModelCatalog.ParseModels(null, "OpenCode").ShouldBeEmpty();
    }

    private static JsonElement? ParseOptions(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();
}
