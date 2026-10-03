using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20260929-ai-code-provider-chat RF-002/RF-005/RF-009: parsing do
/// protocolo OpenAI-compatível (models, chat completions em SSE com tool
/// calls, images) contra um handler HTTP fake.
/// </summary>
public sealed class OpenAiCompatibleClientTests : IDisposable
{
    private const string BaseUrl = "http://provider.test";

    private readonly List<HttpClient> _httpClients = [];

    private OpenAiCompatibleClient Client(FakeHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        _httpClients.Add(http);
        return new OpenAiCompatibleClient(http);
    }

    public void Dispose()
    {
        foreach (var http in _httpClients)
        {
            http.Dispose();
        }
    }

    [Fact]
    public async Task Dado_ProviderComModels_Quando_ListarModels_Entao_IdsOrdenados()
    {
        var handler = new FakeHandler("/v1/models", """
            {"data":[{"id":"z-model"},{"id":"a-model"},{"id":"m-model"}]}
            """);
        var client = Client(handler);

        var models = await client.ListModelsAsync(BaseUrl, "sk-test");

        models.ShouldBe(["a-model", "m-model", "z-model"]);
    }

    [Fact]
    public async Task Dado_ProviderComErro401_Quando_ListarModels_Entao_ChatProviderException()
    {
        var handler = new FakeHandler(("/v1/models", """{"error":"bad key"}""", "application/json", HttpStatusCode.Unauthorized));
        var client = Client(handler);

        var exception = await Should.ThrowAsync<ChatProviderException>(() => client.ListModelsAsync(BaseUrl, "sk-bad"));

        exception.StatusCode.ShouldBe(401);
        exception.Message.Contains("sk-bad", StringComparison.Ordinal).ShouldBeFalse("a key nunca aparece na mensagem de erro");
    }

    [Fact]
    public async Task Dado_StreamComToolCalls_Quando_StreamChat_Entao_DeltasEToolDeltasParseados()
    {
        var sse = new StringBuilder()
            .AppendLine("""data: {"choices":[{"delta":{"content":"Olá"}}]}""")
            .AppendLine("""data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"shell_exec","arguments":"{\"co"}}]}}]}""")
            .AppendLine("""data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"mmand\":\"ls\"}"}}]}}]}""")
            .AppendLine("""data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":10,"completion_tokens":5}}""")
            .AppendLine("data: [DONE]")
            .ToString();
        var handler = new FakeHandler("/v1/chat/completions", sse, contentType: "text/event-stream");
        var client = Client(handler);

        var events = new List<OpenAiStreamEvent>();
        await foreach (var chunk in client.StreamChatAsync(
            BaseUrl, "sk-test", "m1",
            [new OpenAiChatMessage("user", "oi")],
            [new OpenAiToolDefinition("shell_exec", "run", """{"type":"object"}""")]))
        {
            events.Add(chunk);
        }

        events.Count(e => e.ContentDelta == "Olá").ShouldBe(1);
        var toolEvents = events.SelectMany(e => e.ToolCallDeltas ?? []).ToList();
        toolEvents.Count.ShouldBe(2);
        toolEvents[0].Id.ShouldBe("call_1");
        toolEvents[0].Name.ShouldBe("shell_exec");
        string.Concat(toolEvents.Select(t => t.ArgumentsDelta)).ShouldBe("""{"command":"ls"}""");
        events.Any(e => e.FinishReason == "tool_calls" && e.Usage?.PromptTokens == 10).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_StreamComReasoning_Quando_StreamChat_Entao_ReasoningDeltaSeparado()
    {
        // Reasoning models (DeepSeek R1, o-series) emit delta.reasoning_content;
        // chunks also carry content:"" — empty deltas must not count as reply.
        var sse = new StringBuilder()
            .AppendLine("""data: {"choices":[{"delta":{"content":"","reasoning_content":"pensando "}}]}""")
            .AppendLine("""data: {"choices":[{"delta":{"reasoning_content":"mais"}}]}""")
            .AppendLine("""data: {"choices":[{"delta":{"content":"pong"}}]}""")
            .AppendLine("""data: {"choices":[{"delta":{},"finish_reason":"stop"}]}""")
            .AppendLine("data: [DONE]")
            .ToString();
        var handler = new FakeHandler("/v1/chat/completions", sse, contentType: "text/event-stream");
        var client = Client(handler);

        var events = new List<OpenAiStreamEvent>();
        await foreach (var chunk in client.StreamChatAsync(
            BaseUrl, "sk-test", "r1",
            [new OpenAiChatMessage("user", "oi")], null))
        {
            events.Add(chunk);
        }

        events.Select(e => e.ReasoningDelta).Where(r => r is not null)
            .ShouldBe(["pensando ", "mais"]);
        events.Count(e => e.ContentDelta == "pong").ShouldBe(1);
        events.ShouldNotContain(e => e.ContentDelta == "");
    }

    [Fact]
    public async Task Dado_MaxTokensInformado_Quando_StreamChat_Entao_PayloadLevaMaxTokens()
    {
        var handler = new FakeHandler("/v1/chat/completions", "data: [DONE]\n", contentType: "text/event-stream");
        var client = Client(handler);

        await foreach (var unused in client.StreamChatAsync(
            BaseUrl, "sk-test", "m1",
            [new OpenAiChatMessage("user", "oi")], null, maxTokens: 4096))
        {
            _ = unused;
        }

        handler.RequestBodies.Single().ShouldContain("\"max_tokens\":4096");
    }

    [Fact]
    public async Task Dado_ImagemB64_Quando_GenerateImage_Entao_RetornaBase64()
    {
        var handler = new FakeHandler("/v1/images/generations", """
            {"data":[{"b64_json":"QUJD"}]}
            """);
        var client = Client(handler);

        var b64 = await client.GenerateImageAsync(BaseUrl, "sk-test", "img-1", "a cat", null);

        b64.ShouldBe("QUJD");
    }

    [Fact]
    public async Task Dado_ImagemPorUrl_Quando_GenerateImage_Entao_BaixaEPersisteComoBase64()
    {
        var handler = new FakeHandler(
            ("/v1/images/generations", """{"data":[{"url":"http://provider.test/img.png"}]}""", "application/json", HttpStatusCode.OK),
            ("/img.png", "PNGDATA", "image/png", HttpStatusCode.OK));
        var client = Client(handler);

        var b64 = await client.GenerateImageAsync(BaseUrl, "sk-test", "img-1", "a cat", null);

        Convert.FromBase64String(b64).ShouldBe("PNGDATA"u8.ToArray());
    }

    [Fact]
    public async Task Dado_BaseUrlSemV1_Quando_Chamar_Entao_SufixoV1Adicionado()
    {
        var handler = new FakeHandler("/v1/models", """{"data":[]}""");
        var client = Client(handler);

        await client.ListModelsAsync("http://provider.test", "sk-test");

        handler.Requests.Single().RequestUri!.AbsolutePath.ShouldBe("/v1/models");
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly List<(string Path, string Body, string ContentType, HttpStatusCode Status)> _responses;
        public readonly List<HttpRequestMessage> Requests = [];
        public readonly List<string> RequestBodies = [];

        public FakeHandler(string path, string body, string contentType = "application/json")
            : this((path, body, contentType, HttpStatusCode.OK))
        {
        }

        public FakeHandler(params (string Path, string Body, string ContentType, HttpStatusCode Status)[] responses) =>
            _responses = [.. responses];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            var path = request.RequestUri!.AbsolutePath;
            var match = _responses.FirstOrDefault(r => path.EndsWith(r.Path, StringComparison.Ordinal));
            if (match == default)
            {
                match = _responses[0];
            }

            return new HttpResponseMessage(match.Status)
            {
                Content = new StringContent(match.Body, System.Text.Encoding.UTF8, match.ContentType),
            };
        }
    }
}
