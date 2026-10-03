using Shouldly;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Server;

/// <summary>
/// SPEC-20261003-ops-hardening RF-003: reachability probe for the configured
/// RAG MCP server. The connector delegate is stubbed — no network involved.
/// </summary>
public class RagConnectionProbeTests
{
    private static RagConnectionProbe ProbeWith(
        Func<Uri, IReadOnlyDictionary<string, string>?, CancellationToken, Task<int>> connect) =>
        new(connect);

    [Fact]
    public async Task Dado_UrlVazia_Quando_Testa_Entao_RetornaNaoConfigurado()
    {
        var probe = ProbeWith((_, _, _) => throw new InvalidOperationException("não deve conectar"));

        var result = await probe.TestAsync("  ", "key");

        result.Ok.ShouldBeFalse();
        result.Error.ShouldBe("rag-not-configured");
        result.LatencyMs.ShouldBeNull();
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://rag.example")]
    [InlineData("/relative/path")]
    public async Task Dado_UrlInvalida_Quando_Testa_Entao_RetornaInvalidUrl(string url)
    {
        var probe = ProbeWith((_, _, _) => throw new InvalidOperationException("não deve conectar"));

        var result = await probe.TestAsync(url, null);

        result.Ok.ShouldBeFalse();
        result.Error.ShouldBe("invalid-url");
    }

    [Fact]
    public async Task Dado_ServidorAcessivel_Quando_Testa_Entao_RetornaOkComLatenciaETools()
    {
        var probe = ProbeWith((endpoint, headers, _) =>
        {
            endpoint.ShouldBe(new Uri("https://rag.example/mcp"));
            headers.ShouldBeNull();
            return Task.FromResult(7);
        });

        var result = await probe.TestAsync("https://rag.example/mcp", null);

        result.Ok.ShouldBeTrue();
        result.ToolCount.ShouldBe(7);
        result.LatencyMs.ShouldNotBeNull();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_ApiKey_Quando_Testa_Entao_EnviaBearerNosHeaders()
    {
        IReadOnlyDictionary<string, string>? captured = null;
        var probe = ProbeWith((_, headers, _) =>
        {
            captured = headers;
            return Task.FromResult(1);
        });

        await probe.TestAsync("https://rag.example/mcp", "aft_secret123");

        captured.ShouldNotBeNull();
        captured["Authorization"].ShouldBe("Bearer aft_secret123");
    }

    [Fact]
    public async Task Dado_ConexaoFalha_Quando_Testa_Entao_RetornaErroSanitizado()
    {
        var probe = ProbeWith((_, _, _) =>
            throw new HttpRequestException("connection refused to https://rag.example/mcp key=aft_secret123"));

        var result = await probe.TestAsync("https://rag.example/mcp", "aft_secret123");

        result.Ok.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.ShouldNotContain("aft_secret123");
        result.Error.ShouldContain("***");
    }

    [Fact]
    public async Task Dado_ConectorCancelaSemSinalDoCaller_Quando_Testa_Entao_RetornaTimeout()
    {
        var probe = ProbeWith((_, _, _) => throw new OperationCanceledException());

        var result = await probe.TestAsync("https://rag.example/mcp", null);

        result.Ok.ShouldBeFalse();
        result.Error.ShouldBe("timeout");
    }

    [Fact]
    public async Task Dado_CancelamentoDoCaller_Quando_Testa_Entao_Propaga()
    {
        using var cts = new CancellationTokenSource();
        var probe = ProbeWith((_, _, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => probe.TestAsync("https://rag.example/mcp", null, cts.Token));
    }
}
