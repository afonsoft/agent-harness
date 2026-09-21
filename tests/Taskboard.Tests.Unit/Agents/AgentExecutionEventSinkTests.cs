using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Server.Hubs;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20260921-agent-execution-event-pipeline RF-003/RF-005: per-scope
/// sequencing, persisted-max seed, redaction, truncation and gapless replay.
/// </summary>
public sealed class AgentExecutionEventSinkTests
{
    private static (AgentExecutionEventSink Sink, IAgentRunEventRepository Repo) Build(
        long seededSequence = 0)
    {
        var repo = Substitute.For<IAgentRunEventRepository>();
        repo.GetMaxSequenceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(seededSequence);

        var services = new ServiceCollection();
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var hub = Substitute.For<IHubContext<AgentLogHub>>();
        var clients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        proxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        clients.Group(Arg.Any<string>()).Returns(proxy);
        hub.Clients.Returns(clients);

        var sink = new AgentExecutionEventSink(
            provider.GetRequiredService<IServiceScopeFactory>(),
            hub,
            new Taskboard.Integrations.Harness.Security.SecretScrubber(),
            NullLogger<AgentExecutionEventSink>.Instance);

        return (sink, repo);
    }

    private static AgentExecutionEvent Evento(string scopeId, string kind = "output", string? payload = null) =>
        new(string.Empty, "run", scopeId, 0, DateTimeOffset.UtcNow, kind, PayloadJson: payload);

    [Fact]
    public async Task Dado_DoisEventos_Quando_Emitidos_Entao_SequenciasCrescentes()
    {
        var (sink, repo) = Build();

        var e1 = await sink.EmitAsync(Evento("r1"));
        var e2 = await sink.EmitAsync(Evento("r1"));

        e1.Sequence.ShouldBe(1);
        e2.Sequence.ShouldBe(2);
        e1.EventId.ShouldNotBeNullOrEmpty();
        await repo.Received(2).AppendAsync(Arg.Any<AgentExecutionEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_EscopoComHistorico_Quando_Emite_Entao_SeedDoMaximoPersistido()
    {
        var (sink, _) = Build(seededSequence: 41);

        var evt = await sink.EmitAsync(Evento("r-old"));

        evt.Sequence.ShouldBe(42);
    }

    [Fact]
    public async Task Dado_EscoposDistintos_Quando_Emitidos_Entao_SequenciasIndependentes()
    {
        var (sink, _) = Build();

        var a = await sink.EmitAsync(Evento("a"));
        var b = await sink.EmitAsync(Evento("b"));

        a.Sequence.ShouldBe(1);
        b.Sequence.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_PayloadComSegredo_Quando_Emite_Entao_PersistidoRedacted()
    {
        var (sink, repo) = Build();
        var token = $"ghp_{new string('a', 40)}";

        await sink.EmitAsync(Evento("r1", "output", $"{{\"log\":\"token {token}\"}}"));

        await repo.Received(1).AppendAsync(
            Arg.Is<AgentExecutionEvent>(e => e.PayloadJson != null && !e.PayloadJson.Contains(token)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_PayloadGigante_Quando_Emite_Entao_Truncado()
    {
        var (sink, repo) = Build();
        var huge = new string('x', AgentExecutionEventSink.MaxPayloadChars + 100);

        await sink.EmitAsync(Evento("r1", "output", huge));

        await repo.Received(1).AppendAsync(
            Arg.Is<AgentExecutionEvent>(e =>
                e.PayloadJson != null
                && e.PayloadJson.Length <= AgentExecutionEventSink.MaxPayloadChars + 20
                && e.PayloadJson.EndsWith("…[truncated]")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_PersistenciaFalha_Quando_Emite_Entao_NaoPropagaExcecao()
    {
        var repo = Substitute.For<IAgentRunEventRepository>();
        repo.AppendAsync(Arg.Any<AgentExecutionEvent>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("db down"));

        var services = new ServiceCollection();
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();
        var hub = Substitute.For<IHubContext<AgentLogHub>>();
        var proxy = Substitute.For<IClientProxy>();
        proxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(proxy);
        hub.Clients.Returns(clients);

        var sink = new AgentExecutionEventSink(
            provider.GetRequiredService<IServiceScopeFactory>(),
            hub,
            new Taskboard.Integrations.Harness.Security.SecretScrubber(),
            NullLogger<AgentExecutionEventSink>.Instance);

        var evt = await sink.EmitAsync(Evento("r1"));

        evt.Sequence.ShouldBe(1);
        evt.EventId.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Dado_PersistenciaFalha_Quando_Emite_Entao_SequenciaReutilizadaESemBroadcast()
    {
        var repo = Substitute.For<IAgentRunEventRepository>();
        repo.AppendAsync(Arg.Any<AgentExecutionEvent>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("db down"));

        var services = new ServiceCollection();
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();
        var hub = Substitute.For<IHubContext<AgentLogHub>>();
        var proxy = Substitute.For<IClientProxy>();
        proxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(proxy);
        hub.Clients.Returns(clients);

        var sink = new AgentExecutionEventSink(
            provider.GetRequiredService<IServiceScopeFactory>(),
            hub,
            new Taskboard.Integrations.Harness.Security.SecretScrubber(),
            NullLogger<AgentExecutionEventSink>.Instance);

        var failed = await sink.EmitAsync(Evento("r1"));
        failed.Sequence.ShouldBe(1);

        // Next event reuses sequence 1 — replay can never have a permanent gap.
        repo.ClearReceivedCalls();
        repo.AppendAsync(Arg.Any<AgentExecutionEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var next = await sink.EmitAsync(Evento("r1"));

        next.Sequence.ShouldBe(1);
        await proxy.Received(1).SendCoreAsync(
            "ReceiveAgentEvent", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }
}
