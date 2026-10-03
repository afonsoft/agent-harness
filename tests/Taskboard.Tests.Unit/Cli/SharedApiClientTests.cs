using System.Net;
using System.Text;
using Shouldly;
using Taskboard.Cli.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Cli;

/// <summary>
/// SPEC-20261003-ops-hardening RF-002: the HTTP transport moved to a single
/// shared client (Domain.Shared); Cli and Mcp only map errors/results.
/// These tests pin the per-consumer mapping contract via a stub handler.
/// </summary>
public class SharedApiClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public HttpRequestMessage? LastRequest;

        public StubHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        private readonly Exception _error;
        public FailingHandler(Exception error) => _error = error;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(_error);
    }

    private static TaskboardApiClient CliClient(HttpResponseMessage response) =>
        new("http://localhost:1", apiKey: null, handler: new StubHandler(response));

    private static Taskboard.Mcp.Services.TaskboardApiClient McpClient(HttpResponseMessage response) =>
        new("http://localhost:1", apiKey: null, handler: new StubHandler(response));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Dado_Cli_Quando_409_Entao_CliExceptionComExitCode5()
    {
        var client = CliClient(Json(HttpStatusCode.Conflict, """{"error":{"message":"version conflict"}}"""));

        var ex = await Should.ThrowAsync<CliException>(() => client.GetAsync("/api/x", CancellationToken.None));

        ex.ExitCode.ShouldBe(5);
        ex.Message.ShouldContain("version conflict");
    }

    [Fact]
    public async Task Dado_Cli_Quando_401_Entao_CliExceptionComExitCode4()
    {
        var client = CliClient(Json(HttpStatusCode.Unauthorized, """{"message":"nope"}"""));

        var ex = await Should.ThrowAsync<CliException>(() => client.GetAsync("/api/x", CancellationToken.None));

        ex.ExitCode.ShouldBe(4);
    }

    [Fact]
    public async Task Dado_Cli_Quando_ErroDeTransporte_Entao_CliExceptionComExitCode3()
    {
        var failing = new TaskboardApiClient(
            "http://localhost:1", apiKey: null,
            handler: new FailingHandler(new HttpRequestException("connection refused")));

        var ex = await Should.ThrowAsync<CliException>(() => failing.GetAsync("/api/x", CancellationToken.None));

        ex.ExitCode.ShouldBe(3);
        ex.Message.ShouldContain("Servidor indisponível");
    }

    [Fact]
    public async Task Dado_Mcp_Quando_CorpoVazio_Entao_RetornaJsonObjectVazio()
    {
        var client = McpClient(new HttpResponseMessage(HttpStatusCode.OK));

        var result = await client.PostAsync("/api/x", new { }, CancellationToken.None);

        var node = result.ShouldNotBeNull();
        node.AsObject().Count.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_Mcp_Quando_500_Entao_InvalidOperationComStatusEBody()
    {
        var client = McpClient(Json(HttpStatusCode.InternalServerError, "boom"));

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => client.GetAsync("/api/x", CancellationToken.None));

        ex.Message.ShouldContain("500");
        ex.Message.ShouldContain("boom");
    }

    [Fact]
    public async Task Dado_ApiKey_Quando_Configurada_Entao_HeaderXApiKeyEnviado()
    {
        var handler = new StubHandler(Json(HttpStatusCode.OK, "{}"));
        var client = new TaskboardApiClient("http://localhost:1", "my-key", handler);

        await client.GetAsync("/api/x", CancellationToken.None);

        var request = handler.LastRequest.ShouldNotBeNull();
        request.Headers.GetValues("X-Api-Key").ShouldBe(["my-key"]);
    }
}
