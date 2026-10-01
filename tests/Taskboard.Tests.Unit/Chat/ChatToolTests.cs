using System.Text.Json;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Chat.Tools;
using Taskboard.Integrations.Harness.Security;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20260929-ai-code-provider-chat RF-006/RF-007/RF-008: tools auto
/// confinadas — recusa pelo security gateway, path jail, scrubbing de
/// segredos e saída truncada.
/// </summary>
public sealed class ChatToolTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"chat-tools-{Guid.NewGuid():N}");

    public ChatToolTests()
    {
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private static ChatToolContext Context() => new(
        WorkspacePath: "",
        ProviderId: Guid.NewGuid(),
        ProviderBaseUrl: "http://p.test",
        ProviderApiKey: "sk-x",
        ImageModel: "",
        SearchBackend: "none",
        SearchUrl: "",
        SearchApiKey: "");

    private ChatToolContext WorkspaceContext() => Context() with { WorkspacePath = _workspace };

    [Fact]
    public async Task Dado_ComandoPerigoso_Quando_ShellExec_Entao_RecusadoPeloGateway()
    {
        var tool = new ShellExecTool(new DynamicCommandClassifier(), new SecretScrubber());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"command":"sudo rm -rf /"}""").RootElement,
            WorkspaceContext(),
            CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.Contains("refused", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ComandoSeguro_Quando_ShellExec_Entao_ExecutaEScrubaSegredos()
    {
        var tool = new ShellExecTool(new DynamicCommandClassifier(), new SecretScrubber());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"command":"echo ghp_1234567890abcdef1234567890abcdef123456"}""").RootElement,
            WorkspaceContext(),
            CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.Contains("ghp_1234567890abcdef1234567890abcdef123456", StringComparison.Ordinal).ShouldBeFalse("segredo deve ser scrubado (RF-006)");
        result.Json.Contains("exitCode", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_PathForaDoWorkspace_Quando_WriteFile_Entao_BloqueadoPeloPathJail()
    {
        var tool = new WriteFileTool();

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse($$"""{"path":"../../etc/evil.txt","content":"x"}""").RootElement,
            WorkspaceContext(),
            CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.Contains("escapes sandbox", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_PathInterno_Quando_WriteFileReadFile_Entao_CicloCompleto()
    {
        var write = new WriteFileTool();
        var read = new ReadFileTool(new SecretScrubber());

        var written = await write.ExecuteAsync(
            JsonDocument.Parse("""{"path":"docs/nota.md","content":"conteudo"}""").RootElement,
            WorkspaceContext(),
            CancellationToken.None);
        var readBack = await read.ExecuteAsync(
            JsonDocument.Parse("""{"path":"docs/nota.md"}""").RootElement,
            WorkspaceContext(),
            CancellationToken.None);

        written.Refused.ShouldBeFalse();
        File.Exists(Path.Combine(_workspace, "docs", "nota.md")).ShouldBeTrue();
        readBack.Refused.ShouldBeFalse();
        readBack.Json.ShouldContain("conteudo");
    }

    [Fact]
    public async Task Dado_SemBackendConfigurado_Quando_WebSearch_Entao_ErroLegivel()
    {
        var tool = new WebSearchTool((_, _, _) => null);

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"query":"harness"}""").RootElement,
            Context() with { SearchBackend = "none" },
            CancellationToken.None);

        result.Json.Contains("not configured", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_BackendFake_Quando_WebSearch_Entao_ResultadosComoToolOutput()
    {
        var backend = new FakeSearchBackend([
            new ChatSearchResult("T1", "https://a.test/1", "snippet 1"),
            new ChatSearchResult("T2", "https://a.test/2", "snippet 2"),
        ]);
        var tool = new WebSearchTool((kind, _, _) => kind == "tavily" ? backend : null);

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"query":"harness"}""").RootElement,
            Context() with { SearchBackend = "tavily" },
            CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.Contains("https://a.test/1", StringComparison.Ordinal).ShouldBeTrue();
        result.Json.Contains("snippet 2", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ConfigTrocadaEmRuntime_Quando_WebSearch_Entao_ResolveBackendAtual()
    {
        // B-18: o backend é resolvido por execução com os valores atuais do
        // contexto — trocar SearchBackend em Settings não exige restart.
        var backend = new FakeSearchBackend([new ChatSearchResult("T", "https://a.test", "s")]);
        var resolverCalls = 0;
        var tool = new WebSearchTool((kind, url, key) =>
        {
            resolverCalls++;
            return kind == "brave" && url == "https://x" && key == "k" ? backend : null;
        });

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"query":"harness"}""").RootElement,
            Context() with { SearchBackend = "brave", SearchUrl = "https://x", SearchApiKey = "k" },
            CancellationToken.None);

        result.Refused.ShouldBeFalse();
        resolverCalls.ShouldBe(1);
        result.Json.Contains("https://a.test", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_CliNaoAllowlist_Quando_RunCli_Entao_Recusado()
    {
        var tool = new RunCliTool(new SecretScrubber());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"cli":"curl","args":["http://evil.test"]}""").RootElement,
            WorkspaceContext(),
            CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.Contains("not an allowlisted", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_LinguagemNaoSuportada_Quando_CodeInterpreter_Entao_ErroLegivel()
    {
        var tool = new CodeInterpreterTool(new SecretScrubber());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"language":"ruby","code":"puts 1"}""").RootElement,
            WorkspaceContext(),
            CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.Contains("unsupported language", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_SnippetPython_Quando_CodeInterpreter_Entao_ExecutaComTimeoutEScrub()
    {
        var tool = new CodeInterpreterTool(new SecretScrubber());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"language":"python3","code":"print('ok', 'ghp_1234567890abcdef1234567890abcdef123456')"}""").RootElement,
            WorkspaceContext(),
            CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.Contains("ghp_1234567890abcdef1234567890abcdef123456", StringComparison.Ordinal).ShouldBeFalse();
        result.Json.Contains("exitCode", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_WorkspaceComEspacos_Quando_CodeInterpreter_Entao_ArgvInteiro()
    {
        // B-19: `Replace(ph, file).Split(' ')` quebrava o path em espaços —
        // agora o placeholder vira um único elemento do argv.
        var spaced = Path.Combine(_workspace, "dir with spaces");
        Directory.CreateDirectory(spaced);
        var tool = new CodeInterpreterTool(new SecretScrubber());

        var result = await tool.ExecuteAsync(
            JsonDocument.Parse("""{"language":"python3","code":"print('spaces-ok')"}""").RootElement,
            WorkspaceContext() with { WorkspacePath = spaced },
            CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.Contains("spaces-ok", StringComparison.Ordinal).ShouldBeTrue();
        result.Json.Contains("\"exitCode\":0", StringComparison.Ordinal).ShouldBeTrue();
    }

    private sealed class FakeSearchBackend(IReadOnlyList<ChatSearchResult> results) : ISearchBackend
    {
        public Task<IReadOnlyList<ChatSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) =>
            Task.FromResult(results);
    }
}
