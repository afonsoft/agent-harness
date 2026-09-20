using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Integrations.Terminal;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Terminal;

/// <summary>
/// Unit tests for <see cref="TerminalSessionManager"/> using fake
/// <see cref="IPtySession"/>s — no real PTY needed (SPEC-20260917-terminal-tabs).
/// </summary>
public class TerminalSessionManagerTests
{
    private sealed class FakePtySession : IPtySession
    {
        public event Action<string>? OutputReceived;
        public event Action<int>? Exited;

        public DateTimeOffset LastActivityUtc { get; set; } = DateTimeOffset.UtcNow;
        public bool IsRunning { get; private set; }
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public List<string> Written { get; } = new();

        public void Start() { Started = true; IsRunning = true; }
        public Task WriteAsync(string data) { Written.Add(data); return Task.CompletedTask; }
        public Task ResizeAsync(int cols, int rows) => Task.CompletedTask;

        public void EmitOutput(string chunk) => OutputReceived?.Invoke(chunk);
        public void EmitExit(int code = 0) { IsRunning = false; Exited?.Invoke(code); }

        public ValueTask DisposeAsync() { Disposed = true; IsRunning = false; return ValueTask.CompletedTask; }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public List<FakePtySession> Sessions { get; } = new();
        public List<(string SessionId, string Chunk)> Outputs { get; } = new();
        public List<(string SessionId, string Reason)> Closed { get; } = new();
        public TerminalSessionManager Manager { get; }

        public Harness(TimeSpan? idleTimeout = null, TimeSpan? orphanTimeout = null)
        {
            Manager = new TerminalSessionManager(
                () =>
                {
                    var s = new FakePtySession();
                    Sessions.Add(s);
                    return s;
                },
                NullLogger<TerminalSessionManager>.Instance,
                idleTimeout,
                sweepInterval: TimeSpan.FromHours(1),
                orphanTimeout);
        }

        public Func<string, string, Task> OnOutput =>
            (id, chunk) => { Outputs.Add((id, chunk)); return Task.CompletedTask; };

        public Func<string, string, Task> OnClosed =>
            (id, reason) => { Closed.Add((id, reason)); return Task.CompletedTask; };

        public async ValueTask DisposeAsync() => await Manager.DisposeAsync();
    }

    [Fact]
    public async Task Dado_UsuarioSemSessao_Quando_Open_Entao_RetornaSessionIdEIniciaPty()
    {
        await using var h = new Harness();

        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);

        id.ShouldNotBeNullOrEmpty();
        h.Sessions.ShouldHaveSingleItem().Started.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_DuasSessoes_Quando_Output_Entao_RoteiaPelaSessionId()
    {
        await using var h = new Harness();
        var id1 = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);
        var id2 = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);

        h.Sessions[0].EmitOutput("aaa");
        h.Sessions[1].EmitOutput("bbb");

        h.Outputs.ShouldBe([(id1, "aaa"), (id2, "bbb")]);
    }

    [Fact]
    public async Task Dado_SessaoAberta_Quando_InputDeOutraConexao_Entao_Ignorado()
    {
        await using var h = new Harness();
        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);

        await h.Manager.InputAsync("u1", "conn-outra", id, "x");
        await h.Manager.InputAsync("u2", "conn1", id, "y");
        await h.Manager.InputAsync("u1", "conn1", id, "z");

        h.Sessions[0].Written.ShouldBe(["z"]);
    }

    [Fact]
    public async Task Dado_OitoSessoes_Quando_Nona_Entao_InvalidOperation()
    {
        await using var h = new Harness();
        for (var i = 0; i < TerminalSessionManager.MaxSessionsPerUser; i++)
        {
            await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);
        }

        await Should.ThrowAsync<InvalidOperationException>(
            () => h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed));

        // Outro usuário ainda pode abrir.
        await h.Manager.OpenAsync("u2", "conn2", h.OnOutput, h.OnClosed);
        h.Sessions.Count.ShouldBe(9);
    }

    [Fact]
    public async Task Dado_SessaoAberta_Quando_ProcessoSai_Entao_NotificaExitedEDescarta()
    {
        await using var h = new Harness();
        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);

        h.Sessions[0].EmitExit();
        await Task.Delay(50);

        h.Closed.ShouldBe([(id, "exited")]);
        h.Sessions[0].Disposed.ShouldBeTrue();

        // Sessão removida: input vira no-op.
        await h.Manager.InputAsync("u1", "conn1", id, "x");
        h.Sessions[0].Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_SessaoOciosa_Quando_Sweep_Entao_NotificaIdleTimeout()
    {
        await using var h = new Harness(idleTimeout: TimeSpan.FromMinutes(30));
        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);
        h.Sessions[0].LastActivityUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(31);

        await h.Manager.SweepIdleAsync();

        h.Closed.ShouldBe([(id, "idle-timeout")]);
        h.Sessions[0].Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_SessaoAtiva_Quando_Sweep_Entao_Permanece()
    {
        await using var h = new Harness(idleTimeout: TimeSpan.FromMinutes(30));
        await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);

        await h.Manager.SweepIdleAsync();

        h.Closed.ShouldBeEmpty();
        h.Sessions[0].Disposed.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_SessaoDeOutraConexao_Quando_Disconnect_Entao_OrfaSemDispor()
    {
        await using var h = new Harness();
        await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);
        await h.Manager.OpenAsync("u1", "conn2", h.OnOutput, h.OnClosed);

        await h.Manager.OrphanAllForConnectionAsync("conn1");

        // A sessão órfã continua viva — PTY preservado para reattach.
        h.Sessions[0].Disposed.ShouldBeFalse();
        h.Sessions[1].Disposed.ShouldBeFalse();
        h.Closed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_SessaoOrfa_Quando_Input_Entao_IgnoradoAteReattach()
    {
        await using var h = new Harness();
        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);
        await h.Manager.OrphanAllForConnectionAsync("conn1");

        await h.Manager.InputAsync("u1", "conn1", id, "x");

        h.Sessions[0].Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_SessaoOrfa_Quando_Reattach_Entao_InputVoltaEFluxoRebindado()
    {
        await using var h = new Harness();
        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);
        await h.Manager.OrphanAllForConnectionAsync("conn1");

        var newOutputs = new List<(string, string)>();
        var reattached = await h.Manager.ReattachAsync(
            "u1", "conn-nova", id,
            (sid, chunk) => { newOutputs.Add((sid, chunk)); return Task.CompletedTask; },
            h.OnClosed);

        reattached.ShouldBeTrue();
        await h.Manager.InputAsync("u1", "conn-nova", id, "echo ok");
        h.Sessions[0].Written.ShouldBe(["echo ok"]);

        // Callbacks rebindados: output vai para a conexão nova.
        h.Sessions[0].EmitOutput("ok");
        newOutputs.ShouldBe([(id, "ok")]);
        h.Outputs.ShouldBeEmpty(); // callback antigo não recebe mais
    }

    [Fact]
    public async Task Dado_SessaoOrfaDeOutroUsuario_Quando_Reattach_Entao_Falso()
    {
        await using var h = new Harness();
        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);
        await h.Manager.OrphanAllForConnectionAsync("conn1");

        var reattached = await h.Manager.ReattachAsync("u2", "conn-x", id, h.OnOutput, h.OnClosed);

        reattached.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_SessaoInexistente_Quando_Reattach_Entao_Falso()
    {
        await using var h = new Harness();

        (await h.Manager.ReattachAsync("u1", "conn1", "nao-existe", h.OnOutput, h.OnClosed))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_OrfaExpirada_Quando_Sweep_Entao_RemovidaComoConnectionLost()
    {
        // OrphanTimeout zero = qualquer órfã expira no próximo sweep.
        await using var h = new Harness(orphanTimeout: TimeSpan.Zero);
        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);
        await h.Manager.OrphanAllForConnectionAsync("conn1");

        await h.Manager.SweepIdleAsync();

        h.Closed.ShouldBe([(id, "connection lost")]);
        h.Sessions[0].Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_SessaoAberta_Quando_ClosePeloCliente_Entao_DescartaSemCallback()
    {
        await using var h = new Harness();
        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);

        await h.Manager.CloseAsync("u1", "conn1", id);

        h.Sessions[0].Disposed.ShouldBeTrue();
        h.Closed.ShouldBeEmpty(); // cliente já sabe — sem callback redundante
    }

    [Fact]
    public async Task Dado_CloseIdDesconhecido_Quando_Close_Entao_NoOp()
    {
        await using var h = new Harness();
        await h.Manager.CloseAsync("u1", "conn1", "nao-existe");
        h.Closed.ShouldBeEmpty();
    }
}
