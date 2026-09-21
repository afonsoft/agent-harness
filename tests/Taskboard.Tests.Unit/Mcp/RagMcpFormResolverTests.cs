using Shouldly;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Application.Contracts.Mcp;
using Xunit;

namespace Taskboard.Tests.Unit.Mcp;

public class RagMcpFormResolverTests
{
    [Fact]
    public void Dado_OverrideNoBanco_Quando_Resolver_Entao_UrlPreenchidaComFonteDb()
    {
        var state = RagMcpFormResolver.Resolve(
            [Entry(RagMcpFormResolver.UrlKey, "https://rag.example.com/mcp", "db")],
            status: null);

        state.Url.ShouldBe("https://rag.example.com/mcp");
        state.UrlSource.ShouldBe("db");
    }

    [Fact]
    public void Dado_SemConfiguracao_Quando_Resolver_Entao_UrlNulaENomeDefault()
    {
        var state = RagMcpFormResolver.Resolve([], status: null);

        state.Url.ShouldBeNull();
        state.ServerName.ShouldBe("knowledge");
        state.ApiKeySet.ShouldBeFalse();
    }

    [Fact]
    public void Dado_CatalogoSemChaves_ComStatusProvisionado_Quando_Resolver_Entao_FallbackDoStatus()
    {
        var status = new McpProvisionStatus(
            McpProvisionState.Succeeded, null, null,
            ServerName: "knowledge",
            ConfiguredUrl: "https://rag.afonsoft.dev/mcp",
            Agents: [], Error: null);

        var state = RagMcpFormResolver.Resolve([], status);

        state.Url.ShouldBe("https://rag.afonsoft.dev/mcp");
        state.UrlSource.ShouldBe("status");
        state.ServerName.ShouldBe("knowledge");
        state.ServerNameSource.ShouldBe("status");
    }

    [Fact]
    public void Dado_CatalogoComUrl_EStatusDivergente_Quando_Resolver_Entao_CatalogoVence()
    {
        var status = new McpProvisionStatus(
            McpProvisionState.Succeeded, null, null,
            ServerName: "other", ConfiguredUrl: "https://other/mcp", Agents: [], Error: null);

        var state = RagMcpFormResolver.Resolve(
            [Entry(RagMcpFormResolver.UrlKey, "https://db-wins/mcp", "db")], status);

        state.Url.ShouldBe("https://db-wins/mcp");
        state.UrlSource.ShouldBe("db");
    }

    [Fact]
    public void Dado_ApiKeyMascarada_Quando_Resolver_Entao_ApiKeySetSemExporValor()
    {
        var state = RagMcpFormResolver.Resolve(
            [Entry(RagMcpFormResolver.ApiKeyKey, "••••", "db")],
            status: null);

        state.ApiKeySet.ShouldBeTrue();
    }

    [Fact]
    public void Dado_UrlDeEnv_Quando_Resolver_Entao_FonteEnv()
    {
        var state = RagMcpFormResolver.Resolve(
            [Entry(RagMcpFormResolver.UrlKey, "https://env/mcp", "env")],
            status: null);

        state.UrlSource.ShouldBe("env");
    }

    private static ConfigurationEntryDto Entry(string key, string? value, string source) =>
        new(key, value, source, Editable: true, RequiresRestart: false, Masked: false, ReadOnlyReason: null);
}
