using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Chat.Tools;
using Taskboard.Integrations.Harness.Security;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261001-ai-chat-openwebui RF-003: novas tools no padrão open-webui —
/// fetch_url, current_datetime, calculator e memory (persistente e scrubada).
/// </summary>
public sealed class ChatOpenWebuiToolTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), $"chat-owui-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
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

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Dado_ExpressaoAritmetica_Quando_Calculator_Entao_ResultadoExato()
    {
        var tool = new CalculatorTool();

        var result = await tool.ExecuteAsync(Args("""{"expression":"2+3*4"}"""), Context(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("\"result\":14");
    }

    [Fact]
    public async Task Dado_ExpressaoComFuncoes_Quando_Calculator_Entao_ResultadoExato()
    {
        var tool = new CalculatorTool();

        var result = await tool.ExecuteAsync(
            Args("""{"expression":"sqrt(16)+pow(2,3)"}"""), Context(), CancellationToken.None);

        result.Json.ShouldContain("\"result\":12");
    }

    [Fact]
    public async Task Dado_ExpressaoInvalida_Quando_Calculator_Entao_ErroLegivel()
    {
        var tool = new CalculatorTool();

        var result = await tool.ExecuteAsync(Args("""{"expression":"2+@@@"}"""), Context(), CancellationToken.None);

        result.Json.ShouldContain("error");
    }

    [Fact]
    public async Task Dado_TimezoneUtc_Quando_CurrentDatetime_Entao_IsoUnixEUtc()
    {
        var tool = new DateTimeTool();

        var result = await tool.ExecuteAsync(Args("""{"timezone":"utc"}"""), Context(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("\"timezone\":\"UTC\"");
        result.Json.ShouldContain("\"unix\":");
    }

    [Fact]
    public async Task Dado_TimezoneInvalida_Quando_CurrentDatetime_Entao_Recusado()
    {
        var tool = new DateTimeTool();

        var result = await tool.ExecuteAsync(
            Args("""{"timezone":"Nowhere/Fake"}"""), Context(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("unknown timezone");
    }

    [Fact]
    public async Task Dado_UrlNaoHttp_Quando_FetchUrl_Entao_Recusado()
    {
        using var http = new HttpClient(new FakeFetchHandler(""));
        var tool = new FetchUrlTool(http);

        var result = await tool.ExecuteAsync(Args("""{"url":"file:///etc/passwd"}"""), Context(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("http(s)");
    }

    [Fact]
    public async Task Dado_PaginaHtml_Quando_FetchUrl_Entao_ConteudoLegivel()
    {
        const string html = "<html><head><script>alert(1)</script></head>"
            + "<body><h1>Titulo</h1><p>paragrafo util</p></body></html>";
        using var http = new HttpClient(new FakeFetchHandler(html, "text/html"));
        var tool = new FetchUrlTool(http);

        var result = await tool.ExecuteAsync(
            Args("""{"url":"https://exemplo.test/pagina"}"""), Context(), CancellationToken.None);

        result.Refused.ShouldBeFalse();
        result.Json.ShouldContain("Titulo");
        result.Json.ShouldContain("paragrafo util");
        result.Json.ShouldNotContain("alert(1)");
        result.Json.ShouldContain("\"status\":200");
    }

    [Fact]
    public async Task Dado_AcaoAddSearchDelete_Quando_Memory_Entao_CicloCompleto()
    {
        var tool = new MemoryTool(new ChatMemoryStore(_dir));

        var add = await tool.ExecuteAsync(
            Args("""{"action":"add","content":"o host roda Ubuntu"}"""), Context(), CancellationToken.None);
        add.Json.ShouldContain("\"saved\":true");

        var search = await tool.ExecuteAsync(
            Args("""{"action":"search","query":"Ubuntu"}"""), Context(), CancellationToken.None);
        search.Json.ShouldContain("o host roda Ubuntu");

        var id = JsonDocument.Parse(add.Json).RootElement
            .GetProperty("memory").GetProperty("id").GetGuid().ToString();
        var delete = await tool.ExecuteAsync(
            Args($$"""{"action":"delete","id":"{{id}}"}"""), Context(), CancellationToken.None);
        delete.Json.ShouldContain("\"deleted\":true");

        var list = await tool.ExecuteAsync(
            Args("""{"action":"list"}"""), Context(), CancellationToken.None);
        list.Json.ShouldContain("\"count\":0");
    }

    [Fact]
    public async Task Dado_ConteudoComSegredo_Quando_MemoryAdd_Entao_Scrubado()
    {
        var tool = new MemoryTool(new ChatMemoryStore(_dir), new SecretScrubber());

        var result = await tool.ExecuteAsync(
            Args("""{"action":"add","content":"token ghp_1234567890abcdef1234567890abcdef123456"}"""),
            Context(), CancellationToken.None);

        result.Json.Contains("ghp_1234567890abcdef1234567890abcdef123456", StringComparison.Ordinal)
            .ShouldBeFalse("memória não persiste segredos (RF-006)");
    }

    [Fact]
    public async Task Dado_AcaoDesconhecida_Quando_Memory_Entao_Recusado()
    {
        var tool = new MemoryTool(new ChatMemoryStore(_dir));

        var result = await tool.ExecuteAsync(
            Args("""{"action":"explode"}"""), Context(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("unknown action");
    }

    private sealed class FakeFetchHandler(string body, string mediaType = "text/html") : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType),
            };
        }
    }
}
