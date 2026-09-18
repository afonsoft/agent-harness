using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Mcp;
using Taskboard.Integrations.Mcp;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Mcp;

public class McpProvisioningServiceTests : IDisposable
{
    private readonly string _home;

    public McpProvisioningServiceTests()
    {
        _home = Path.Join(Path.GetTempPath(), $"tb-mcp-home-{Guid.NewGuid()}");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private McpProvisioningService CreateService(
        IConfiguration? configuration = null,
        IReadOnlyCollection<AgentType>? enabled = null,
        Func<string, string?>? executableResolver = null,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string Output)>>? agyRunner = null) =>
        new(
            configuration ?? new ConfigurationBuilder().Build(),
            NullLogger<McpProvisioningService>.Instance,
            _home,
            _ => Task.FromResult(enabled ?? (IReadOnlyCollection<AgentType>)Enum.GetValues<AgentType>()),
            executableResolver ?? (_ => null),
            agyRunner);

    private static IConfiguration RagConfig(
        string? name = "knowledge",
        string? url = "https://rag.afonsoft.dev/mcp",
        string? apiKey = "aft_testkey12345") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Taskboard:Rag:ServerName"] = name,
                ["Taskboard:Rag:Url"] = url,
                ["Taskboard:Rag:ApiKey"] = apiKey,
            })
            .Build();

    [Fact]
    public async Task Dado_ConfigRag_Quando_Provision_Entao_EscreveNosCincoClis()
    {
        // Covers AC-1: all five agent configs get the managed entry with Bearer header
        var service = CreateService(RagConfig());

        var status = await service.ProvisionAsync();

        status.State.ShouldBe(McpProvisionState.Succeeded);
        status.Agents.Count.ShouldBe(6);
        status.Agents
            .Where(r => r.Agent != AgentType.Antigravity)
            .ShouldAllBe(r => r.Configured && r.State == McpAgentState.Configured);
        // Antigravity is provisioned via the agy CLI — absent in this test → Skipped.
        status.Agents.Single(r => r.Agent == AgentType.Antigravity)
            .State.ShouldBe(McpAgentState.Skipped);

        var claude = Path.Join(_home, ".claude.json");
        var devin = Path.Join(_home, ".config", "devin", "mcp_config.json");
        var codex = Path.Join(_home, ".codex", "config.toml");
        var opencode = Path.Join(_home, ".config", "opencode", "opencode.json");
        var openhands = Path.Join(_home, ".openhands", "mcp.json");
        foreach (var file in new[] { claude, devin, codex, opencode, openhands })
        {
            File.Exists(file).ShouldBeTrue($"{file} should exist");
        }

        var claudeJson = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(claude))!.AsObject();
        var entry = claudeJson["mcpServers"]!.AsObject()["knowledge"]!.AsObject();
        entry["type"]!.GetValue<string>().ShouldBe("http");
        entry["url"]!.GetValue<string>().ShouldBe("https://rag.afonsoft.dev/mcp");
        entry["headers"]!.AsObject()["Authorization"]!.GetValue<string>()
            .ShouldBe("Bearer aft_testkey12345");

        var opencodeJson = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(opencode))!.AsObject();
        var ocEntry = opencodeJson["mcp"]!.AsObject()["knowledge"]!.AsObject();
        ocEntry["type"]!.GetValue<string>().ShouldBe("remote");
        ocEntry["enabled"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_UrlVazia_Quando_Provision_Entao_RemoveEntryGerenciada()
    {
        // Covers RF-006: cleared URL removes the managed entry only
        var service = CreateService(RagConfig());
        await service.ProvisionAsync();

        var remover = CreateService(
            RagConfig(url: null, apiKey: null));
        var status = await remover.ProvisionAsync();

        status.State.ShouldBe(McpProvisionState.Succeeded);
        status.Agents.ShouldAllBe(r => !r.Configured);
        status.Agents.ShouldContain(r => r.State == McpAgentState.Removed);
        var claudeJson = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Join(_home, ".claude.json")))!.AsObject();
        claudeJson["mcpServers"]!.AsObject().ContainsKey("knowledge").ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_SemApiKey_Quando_Provision_Entao_SemHeaderAuthorization()
    {
        // Covers edge: empty key → no headers
        var service = CreateService(RagConfig(apiKey: null));

        await service.ProvisionAsync();

        var claudeJson = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Join(_home, ".claude.json")))!.AsObject();
        claudeJson["mcpServers"]!.AsObject()["knowledge"]!.AsObject()
            .ContainsKey("headers").ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_AgenteDesconhecido_Quando_Provision_Entao_Skipped()
    {
        // Covers RF-002: unknown/future enum members are skipped with a warning result
        var service = CreateService(RagConfig(), enabled: [(AgentType)999]);

        var status = await service.ProvisionAsync([(AgentType)999]);

        status.Agents.Single(r => r.Agent == (AgentType)999).State.ShouldBe(McpAgentState.Skipped);
    }

    [Fact]
    public async Task Dado_ArquivoCorrompido_Quando_Provision_Entao_Repaired()
    {
        // Covers AC: corrupt config → .corrupt-bak + Repaired result
        var claude = Path.Join(_home, ".claude.json");
        File.WriteAllText(claude, "{ broken !!");
        var service = CreateService(RagConfig());

        var status = await service.ProvisionAsync([AgentType.Claude]);

        var result = status.Agents.Single(r => r.Agent == AgentType.Claude);
        result.State.ShouldBe(McpAgentState.Repaired);
        File.Exists(claude + ".corrupt-bak").ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_EscritaFalha_Quando_Provision_Entao_FalhaIsolada()
    {
        // Covers RF-009: one failing write does not fail the batch
        var devinDir = Path.Join(_home, ".config", "devin");
        Directory.CreateDirectory(devinDir);
        // A directory at the config path makes writes fail.
        Directory.CreateDirectory(Path.Join(devinDir, "mcp_config.json"));

        var service = CreateService(RagConfig());
        var status = await service.ProvisionAsync([AgentType.Devin, AgentType.Claude]);

        status.State.ShouldBe(McpProvisionState.Failed);
        status.Agents.Single(r => r.Agent == AgentType.Devin).State.ShouldBe(McpAgentState.Failed);
        status.Agents.Single(r => r.Agent == AgentType.Claude).State.ShouldBe(McpAgentState.Configured);
    }

    [Fact]
    public void Dado_Status_Quando_Lido_Entao_NaoContemApiKey()
    {
        // Covers RF-009/AC: the key never appears in status payloads or errors
        var service = CreateService(RagConfig());

        var status = service.GetStatus();

        var json = System.Text.Json.JsonSerializer.Serialize(status);
        json.ShouldNotContain("aft_testkey12345");
        status.ConfiguredUrl.ShouldBe("https://rag.afonsoft.dev/mcp");
        status.ServerName.ShouldBe("knowledge");
    }

    [Fact]
    public void Dado_SemArquivos_Quando_GetStatus_Entao_NotConfigured()
    {
        // Covers AC: missing file → configured:false, not an error
        var service = CreateService(RagConfig());

        var status = service.GetStatus();

        status.Agents
            .ShouldAllBe(r => !r.Configured && r.State == McpAgentState.NotConfigured);
    }

    [Fact]
    public async Task Dado_AgyDisponivel_Quando_Provision_Entao_AddComArgsCorretos()
    {
        // Covers RF-005: Antigravity provisioned via `agy mcp add -t http -H ... <name> <url>`
        List<string>? captured = null;
        var service = CreateService(
            RagConfig(),
            executableResolver: exe => exe == "agy" ? "/usr/bin/agy" : null,
            agyRunner: (_, args, _) =>
            {
                captured = [.. args];
                return Task.FromResult((0, "Added MCP server \"knowledge\" (http)"));
            });

        var status = await service.ProvisionAsync([AgentType.Antigravity]);

        status.Agents.Single(r => r.Agent == AgentType.Antigravity).State.ShouldBe(McpAgentState.Configured);
        captured.ShouldNotBeNull();
        captured.ShouldBe([
            "mcp", "add", "-t", "http",
            "-H", "Authorization: Bearer aft_testkey12345",
            "knowledge", "https://rag.afonsoft.dev/mcp"]);
    }

    [Fact]
    public async Task Dado_AgyEntryExistente_Quando_Provision_Entao_NoOpSemChamarCli()
    {
        // Covers idempotency: entry already matching → Configured without spawning agy
        var dir = Path.Join(_home, ".gemini", "config");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Join(dir, "mcp_config.json"), """
            { "mcpServers": { "knowledge": { "serverUrl": "https://rag.afonsoft.dev/mcp" } } }
            """);

        var called = false;
        var service = CreateService(
            RagConfig(),
            executableResolver: _ => "/usr/bin/agy",
            agyRunner: (_, _, _) =>
            {
                called = true;
                return Task.FromResult((0, ""));
            });

        var status = await service.ProvisionAsync([AgentType.Antigravity]);

        status.Agents.Single(r => r.Agent == AgentType.Antigravity).State.ShouldBe(McpAgentState.Configured);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_UrlVaziaEAgyEntryExiste_Quando_Provision_Entao_RemoveViaCli()
    {
        // Covers removal: `agy mcp remove <name>` when the managed entry exists
        var dir = Path.Join(_home, ".gemini", "config");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Join(dir, "mcp_config.json"), """
            { "mcpServers": { "knowledge": { "serverUrl": "https://rag.afonsoft.dev/mcp" } } }
            """);

        List<string>? captured = null;
        var service = CreateService(
            RagConfig(url: null, apiKey: null),
            executableResolver: _ => "/usr/bin/agy",
            agyRunner: (_, args, _) =>
            {
                captured = [.. args];
                return Task.FromResult((0, "Removed MCP server \"knowledge\""));
            });

        var status = await service.ProvisionAsync([AgentType.Antigravity]);

        status.Agents.Single(r => r.Agent == AgentType.Antigravity).State.ShouldBe(McpAgentState.Removed);
        captured.ShouldBe(["mcp", "remove", "knowledge"]);
    }

    [Fact]
    public async Task Dado_AgyFalha_Quando_Provision_Entao_FailedComApiKeySanitizada()
    {
        // Covers RF-009: agy stderr echoing the key must be redacted in results
        var service = CreateService(
            RagConfig(),
            executableResolver: _ => "/usr/bin/agy",
            agyRunner: (_, _, _) => Task.FromResult(
                (1, "denied for Bearer aft_testkey12345")));

        var status = await service.ProvisionAsync([AgentType.Antigravity]);

        var result = status.Agents.Single(r => r.Agent == AgentType.Antigravity);
        result.State.ShouldBe(McpAgentState.Failed);
        result.Error.ShouldNotBeNull().ShouldNotContain("aft_testkey12345");
    }

    [Fact]
    public void Dado_AgyConfigExistente_Quando_GetStatus_Entao_Configured()
    {
        // Covers status read: serverUrl match in mcp_config.json → Configured
        var dir = Path.Join(_home, ".gemini", "config");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Join(dir, "mcp_config.json"), """
            { "mcpServers": { "knowledge": { "serverUrl": "https://rag.afonsoft.dev/mcp" } } }
            """);

        var service = CreateService(RagConfig());

        var status = service.GetStatus();

        var agy = status.Agents.Single(r => r.Agent == AgentType.Antigravity);
        agy.Configured.ShouldBeTrue();
        agy.State.ShouldBe(McpAgentState.Configured);
    }
}
