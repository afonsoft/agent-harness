using System.Text.Json.Nodes;
using Shouldly;
using Taskboard.Integrations.Mcp;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Mcp;

public class JsonConfigMergerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public JsonConfigMergerTests()
    {
        _dir = Path.Join(Path.GetTempPath(), $"tb-mcp-json-{Guid.NewGuid()}");
        _path = Path.Join(_dir, "config.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static JsonObject Entry(string url) => new()
    {
        ["type"] = "http",
        ["url"] = url,
        ["headers"] = new JsonObject { ["Authorization"] = "Bearer k" }
    };

    [Fact]
    public void Dado_ArquivoInexistente_Quando_Upsert_Entao_CriaComEntry()
    {
        var outcome = JsonConfigMerger.Merge(_path, "mcpServers", "knowledge", Entry("https://rag/mcp"));

        outcome.ShouldBe(MergeOutcome.Updated);
        JsonConfigMerger.ReadManagedUrl(_path, "mcpServers", "knowledge").ShouldBe("https://rag/mcp");
    }

    [Fact]
    public void Dado_OutrosServidores_Quando_Upsert_Entao_Preserva()
    {
        // Covers RF-003: only the managed entry is touched
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """
            { "mcpServers": { "other": { "url": "https://other/mcp" } }, "theme": "dark" }
            """);

        JsonConfigMerger.Merge(_path, "mcpServers", "knowledge", Entry("https://rag/mcp"));

        var root = JsonNode.Parse(File.ReadAllText(_path))!.AsObject();
        root["theme"]!.GetValue<string>().ShouldBe("dark");
        root["mcpServers"]!.AsObject()["other"]!.AsObject()["url"]!.GetValue<string>()
            .ShouldBe("https://other/mcp");
        root["mcpServers"]!.AsObject()["knowledge"]!.AsObject()["url"]!.GetValue<string>()
            .ShouldBe("https://rag/mcp");
        File.Exists(_path + ".bak").ShouldBeTrue();
    }

    [Fact]
    public void Dado_EntryIdentica_Quando_Upsert_Entao_NoChange()
    {
        JsonConfigMerger.Merge(_path, "mcpServers", "knowledge", Entry("https://rag/mcp"));
        File.Delete(_path + ".bak");

        var outcome = JsonConfigMerger.Merge(_path, "mcpServers", "knowledge", Entry("https://rag/mcp"));

        outcome.ShouldBe(MergeOutcome.NoChange);
        File.Exists(_path + ".bak").ShouldBeFalse();
    }

    [Fact]
    public void Dado_EntryExistente_Quando_Remove_Entao_RemoveSemTocarOutros()
    {
        // Covers RF-006: removal deletes only the managed entry
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """
            { "mcpServers": { "knowledge": { "url": "https://rag/mcp" }, "other": { "url": "x" } } }
            """);

        var outcome = JsonConfigMerger.Merge(_path, "mcpServers", "knowledge", null);

        outcome.ShouldBe(MergeOutcome.Removed);
        var root = JsonNode.Parse(File.ReadAllText(_path))!.AsObject();
        root["mcpServers"]!.AsObject().ContainsKey("knowledge").ShouldBeFalse();
        root["mcpServers"]!.AsObject().ContainsKey("other").ShouldBeTrue();
    }

    [Fact]
    public void Dado_ArquivoCorrompido_Quando_Upsert_Entao_CorruptBakERepaired()
    {
        // Covers AC: corrupt file → .corrupt-bak + rewritten + Repaired
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "{ not json !!!");

        var outcome = JsonConfigMerger.Merge(_path, "mcpServers", "knowledge", Entry("https://rag/mcp"));

        outcome.ShouldBe(MergeOutcome.Repaired);
        File.ReadAllText(_path + ".corrupt-bak").ShouldBe("{ not json !!!");
        JsonConfigMerger.ReadManagedUrl(_path, "mcpServers", "knowledge").ShouldBe("https://rag/mcp");
    }

    [Fact]
    public void Dado_RemocaoSemEntry_Quando_Remove_Entao_NoChange()
    {
        var outcome = JsonConfigMerger.Merge(_path, "mcpServers", "knowledge", null);

        outcome.ShouldBe(MergeOutcome.NoChange);
        File.Exists(_path).ShouldBeFalse();
    }
}
