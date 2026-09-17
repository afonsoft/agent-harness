using System.Net;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Taskboard.Tests.Integration;

/// <summary>
/// SPEC-20260917-cli-agents-terminal: the agent-CLI status endpoint and the
/// terminal hub are admin-only surfaces — anonymous requests get 401 and the
/// Taskboard:Terminal:Enabled flag gates hub connections.
/// </summary>
public class AgentCliEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private static readonly string? ScriptPath = new[] { "/usr/bin/script", "/bin/script" }
        .FirstOrDefault(File.Exists);

    private readonly TaskboardWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgentCliEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_GetAgentClis_Entao_Retorna401()
    {
        var response = await _client.GetAsync("/api/agent-clis");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_SemCredenciais_Quando_TerminalHubNegotiate_Entao_Retorna401()
    {
        var response = await _client.PostAsync("/terminal-hub/negotiate?negotiateVersion=1", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dado_Autenticado_Quando_GetAgentClis_Entao_RetornaListaCompleta()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/agent-clis");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonArray>();
        body.ShouldNotBeNull();
        body!.Count.ShouldBe(5);
        foreach (var entry in body)
        {
            entry?["displayName"]?.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
            entry?["binary"]?.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
            entry?["loginCommand"]?.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
            entry?["installed"].ShouldNotBeNull();
            entry?["authStatus"].ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Dado_TerminalHabilitado_Quando_ConectaComApiKey_Entao_SessaoDuplex()
    {
        if (ScriptPath is null)
        {
            return; // `script` (bsdutils) unavailable — PTY cannot spawn.
        }

        await using var connection = CreateHubConnection(_factory);
        var output = new StringBuilder();
        var markerSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var marker = $"tb-hub-{Guid.NewGuid():N}";
        connection.On<string>("output", chunk =>
        {
            output.Append(chunk);
            if (output.ToString().Contains(marker, StringComparison.Ordinal))
            {
                markerSeen.TrySetResult();
            }
        });

        await connection.StartAsync();

        connection.State.ShouldBe(HubConnectionState.Connected);

        // The initial prompt may race the first poll — write fresh input instead.
        await connection.InvokeAsync("Input", $"echo {marker}\n");
        var completed = await Task.WhenAny(markerSeen.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        completed.ShouldBe(markerSeen.Task, "o input deve ecoar a saída do bash de volta ao cliente");
        await connection.StopAsync();
    }

    private static HubConnection CreateHubConnection(TaskboardWebApplicationFactory factory) =>
        new HubConnectionBuilder()
            .WithUrl("http://localhost/terminal-hub", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Headers["X-Api-Key"] = TaskboardWebApplicationFactory.TestApiKey;
            })
            .Build();
}

/// <summary>Factory variant with the terminal feature flag disabled.</summary>
public sealed class TerminalDisabledFactory : TaskboardWebApplicationFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Taskboard:Terminal:Enabled", "false");
    }
}

public class TerminalDisabledTests : IClassFixture<TerminalDisabledFactory>
{
    private readonly TerminalDisabledFactory _factory;

    public TerminalDisabledTests(TerminalDisabledFactory factory) => _factory = factory;

    [Fact]
    public async Task Dado_TerminalDesabilitado_Quando_Conecta_Entao_HubRejeita()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/terminal-hub", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers["X-Api-Key"] = TaskboardWebApplicationFactory.TestApiKey;
            })
            .Build();

        // With LongPolling a failure in OnConnectedAsync surfaces as a closed
        // connection (StartAsync itself may complete the transport handshake).
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += ex =>
        {
            closed.TrySetResult(ex);
            return Task.CompletedTask;
        };

        try
        {
            await connection.StartAsync();
        }
        catch (Exception)
        {
            return; // handshake rejected — flag enforced.
        }

        var completed = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.ShouldBe(closed.Task, "o hub deve fechar a conexão quando o terminal está desabilitado");
    }
}
