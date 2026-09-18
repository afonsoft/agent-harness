using Shouldly;
using Taskboard.Integrations.Mcp;
using Tomlyn;
using Tomlyn.Model;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Mcp;

public class TomlConfigMergerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public TomlConfigMergerTests()
    {
        _dir = Path.Join(Path.GetTempPath(), $"tb-mcp-toml-{Guid.NewGuid()}");
        _path = Path.Join(_dir, "config.toml");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private TomlTable Read() =>
        TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(_path)) ?? new TomlTable();

    [Fact]
    public void Dado_ArquivoInexistente_Quando_Upsert_Entao_CriaTabelaComHeaders()
    {
        var outcome = TomlConfigMerger.Merge(_path, "knowledge", "https://rag/mcp", "aft_key123456");

        outcome.ShouldBe(MergeOutcome.Created);
        var root = Read();
        var server = (TomlTable)((TomlTable)root["mcp_servers"])["knowledge"];
        server["url"].ShouldBe("https://rag/mcp");
        ((TomlTable)server["headers"])["Authorization"].ShouldBe("Bearer aft_key123456");
    }

    [Fact]
    public void Dado_SemApiKey_Quando_Upsert_Entao_SemHeaders()
    {
        // Covers RF-002: empty key emits no headers
        TomlConfigMerger.Merge(_path, "knowledge", "https://rag/mcp", null);

        var server = (TomlTable)((TomlTable)Read()["mcp_servers"])["knowledge"];
        server.ContainsKey("headers").ShouldBeFalse();
    }

    [Fact]
    public void Dado_OutrasTabelas_Quando_Upsert_Entao_Preserva()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """
            model = "gpt-5"

            [mcp_servers.other]
            url = "https://other/mcp"
            """);

        TomlConfigMerger.Merge(_path, "knowledge", "https://rag/mcp", "key12345678");

        var root = Read();
        root["model"].ShouldBe("gpt-5");
        ((TomlTable)((TomlTable)root["mcp_servers"])["other"])["url"].ShouldBe("https://other/mcp");
        File.Exists(_path + ".bak").ShouldBeTrue();
    }

    [Fact]
    public void Dado_EntryIdentica_Quando_Upsert_Entao_NoChange()
    {
        TomlConfigMerger.Merge(_path, "knowledge", "https://rag/mcp", "key12345678");
        File.Delete(_path + ".bak");

        var outcome = TomlConfigMerger.Merge(_path, "knowledge", "https://rag/mcp", "key12345678");

        outcome.ShouldBe(MergeOutcome.NoChange);
        File.Exists(_path + ".bak").ShouldBeFalse();
    }

    [Fact]
    public void Dado_EntryComUrlDiferente_Quando_Upsert_Entao_Updated()
    {
        // Covers SPEC-20260918-rag-mcp-sync RF-003: overwrite reports Updated, not Created
        TomlConfigMerger.Merge(_path, "knowledge", "https://old/mcp", "key12345678");

        var outcome = TomlConfigMerger.Merge(_path, "knowledge", "https://rag/mcp", "key12345678");

        outcome.ShouldBe(MergeOutcome.Updated);
        TomlConfigMerger.ReadManagedUrl(_path, "knowledge").ShouldBe("https://rag/mcp");
    }

    [Fact]
    public void Dado_EntryExistente_Quando_Remove_Entao_Removida()
    {
        TomlConfigMerger.Merge(_path, "knowledge", "https://rag/mcp", "key12345678");
        TomlConfigMerger.Merge(_path, "other", "https://other/mcp", null);

        var outcome = TomlConfigMerger.Merge(_path, "knowledge", null, null);

        outcome.ShouldBe(MergeOutcome.Removed);
        var servers = (TomlTable)Read()["mcp_servers"];
        servers.ContainsKey("knowledge").ShouldBeFalse();
        servers.ContainsKey("other").ShouldBeTrue();
    }

    [Fact]
    public void Dado_ArquivoCorrompido_Quando_Upsert_Entao_CorruptBakERepaired()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "[[[ not toml");

        var outcome = TomlConfigMerger.Merge(_path, "knowledge", "https://rag/mcp", null);

        outcome.ShouldBe(MergeOutcome.Repaired);
        File.ReadAllText(_path + ".corrupt-bak").ShouldBe("[[[ not toml");
        TomlConfigMerger.ReadManagedUrl(_path, "knowledge").ShouldBe("https://rag/mcp");
    }

    [Fact]
    public void Dado_Escrita_Quando_Concluida_Entao_Permissao0600()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        TomlConfigMerger.Merge(_path, "knowledge", "https://rag/mcp", "key12345678");

        File.GetUnixFileMode(_path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
