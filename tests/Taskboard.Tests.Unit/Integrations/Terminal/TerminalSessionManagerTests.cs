using Microsoft.Extensions.Logging;
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
        public List<string?> RequestedWorkdirs { get; } = new();
        public List<IReadOnlyList<string>?> RequestedCommands { get; } = new();
        public List<(string SessionId, string Chunk)> Outputs { get; } = new();
        public List<(string SessionId, string Reason)> Closed { get; } = new();
        public RecordingLogger Logger { get; } = new();
        public TerminalSessionManager Manager { get; }

        public Harness(TimeSpan? idleTimeout = null, TimeSpan? orphanTimeout = null, int? scrollbackChars = null)
        {
            Manager = new TerminalSessionManager(
                (workdir, command) =>
                {
                    var s = new FakePtySession();
                    Sessions.Add(s);
                    RequestedWorkdirs.Add(workdir);
                    RequestedCommands.Add(command);
                    return s;
                },
                Logger,
                idleTimeout,
                sweepInterval: TimeSpan.FromHours(1),
                orphanTimeout,
                scrollbackChars);
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

    // SPEC-20260928-ai-code-generic-cli RF-003: deterministic session ids +
    // scrollback replay back AI Code terminal threads.

    [Fact]
    public async Task Dado_RequestedSessionId_Quando_Open_Entao_UsaIdSolicitado()
    {
        await using var h = new Harness();

        var id = await h.Manager.OpenAsync(
            "u1", "conn1", h.OnOutput, h.OnClosed, requestedSessionId: "t-thread-1");

        id.ShouldBe("t-thread-1");
    }

    [Fact]
    public async Task Dado_SessaoViva_Quando_OpenMesmoId_Entao_RebindSemNovaSessaoEReplayScrollback()
    {
        await using var h = new Harness();
        var id = await h.Manager.OpenAsync(
            "u1", "conn1", h.OnOutput, h.OnClosed, requestedSessionId: "t-abc");
        h.Sessions[0].EmitOutput("hello");
        h.Outputs.Clear();

        var second = await h.Manager.OpenAsync(
            "u1", "conn2", h.OnOutput, h.OnClosed, requestedSessionId: "t-abc");

        second.ShouldBe(id);
        h.Sessions.ShouldHaveSingleItem();
        // The rebind replays buffered scrollback through the new delegate.
        h.Outputs.ShouldContain(o => o.SessionId == "t-abc" && o.Chunk.Contains("hello"));
    }

    // SPEC-20260929-pty-session-security RF-001: rebind é exclusivo do dono —
    // um id determinístico de outro usuário não pode ser reassociado.

    [Fact]
    public async Task Dado_SessaoDeOutroUsuario_Quando_OpenMesmoId_Entao_Recusa()
    {
        await using var h = new Harness();
        await h.Manager.OpenAsync(
            "u1", "conn1", h.OnOutput, h.OnClosed, requestedSessionId: "t-abc");

        var act = () => h.Manager.OpenAsync(
            "u2", "conn2", h.OnOutput, h.OnClosed, requestedSessionId: "t-abc");

        (await act.ShouldThrowAsync<InvalidOperationException>())
            .Message.ShouldContain("already in use");
        h.Sessions.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Dado_SessaoOrfaDeOutroUsuario_Quando_OpenMesmoId_Entao_Recusa()
    {
        await using var h = new Harness();
        await h.Manager.OpenAsync(
            "u1", "conn1", h.OnOutput, h.OnClosed, requestedSessionId: "t-abc");
        await h.Manager.OrphanAllForConnectionAsync("conn1");

        var act = () => h.Manager.OpenAsync(
            "u2", "conn2", h.OnOutput, h.OnClosed, requestedSessionId: "t-abc");

        await act.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Dado_SessaoMortaComMesmoId_Quando_Open_Entao_IdReutilizadoComNovaSessao()
    {
        await using var h = new Harness();
        var first = await h.Manager.OpenAsync(
            "u1", "conn1", h.OnOutput, h.OnClosed, requestedSessionId: "t-abc");
        await h.Sessions[0].DisposeAsync();

        var second = await h.Manager.OpenAsync(
            "u1", "conn2", h.OnOutput, h.OnClosed, requestedSessionId: "t-abc");

        second.ShouldBe(first);
        h.Sessions.Count.ShouldBe(2);
        h.Sessions[1].Started.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_RequestedSessionIdInvalido_Quando_Open_Entao_IdGerado()
    {
        await using var h = new Harness();

        var id = await h.Manager.OpenAsync(
            "u1", "conn1", h.OnOutput, h.OnClosed, requestedSessionId: "../../evil");

        id.ShouldNotBe("../../evil");
        id.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Dado_OutputEmitido_Quando_GetScrollback_Entao_RetornaBuffer()
    {
        await using var h = new Harness();
        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);

        h.Sessions[0].EmitOutput("chunk-a");
        h.Sessions[0].EmitOutput("chunk-b");

        h.Manager.GetScrollback(id).ShouldBe("chunk-achunk-b");
    }

    // SPEC-20261001-terminal-memory-mobile RF-002: o buffer do servidor é
    // limitado a 64 KB por sessão por padrão; Terminal:ScrollbackChars ajusta.

    [Fact]
    public async Task Dado_OutputAcimaDoCap_Quando_GetScrollback_Entao_TruncaNoDefault64K()
    {
        await using var h = new Harness();
        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);

        h.Sessions[0].EmitOutput(new string('x', TerminalSessionManager.DefaultScrollbackChars + 500));

        var scrollback = h.Manager.GetScrollback(id);
        scrollback.Length.ShouldBe(TerminalSessionManager.DefaultScrollbackChars);
    }

    [Fact]
    public async Task Dado_CapCustomizado_Quando_OutputExcede_Entao_TruncaNoCapConfigurado()
    {
        await using var h = new Harness(scrollbackChars: 1000);
        var id = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);

        h.Sessions[0].EmitOutput(new string('a', 600));
        h.Sessions[0].EmitOutput(new string('b', 600));

        var scrollback = h.Manager.GetScrollback(id);
        scrollback.Length.ShouldBe(1000);
        scrollback.ShouldEndWith(new string('b', 400));
    }

    // SPEC-20260920-global-repo-selector RF-006 — cwd do repo só para sessões novas.

    [Fact]
    public async Task Dado_Workdir_Quando_Open_Entao_FactoryRecebeWorkdir()
    {
        await using var h = new Harness();

        await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed, "/home/u/repos/x");

        h.RequestedWorkdirs.ShouldBe(["/home/u/repos/x"]);
    }

    [Fact]
    public async Task Dado_SemWorkdir_Quando_Open_Entao_FactoryRecebeNull()
    {
        await using var h = new Harness();

        await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);

        h.RequestedWorkdirs.ShouldBe([null]);
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

    // Issue #412: nenhuma sessão pode desaparecer do journal sem registro de
    // fechamento — o caminho "exited" (NotifyClosedAsync) agora loga como os
    // demais, e o sweep sobrevive a ticks com falha.

    [Fact]
    public async Task Dado_DuasSessoesOrfas_Quando_UmaSaiESweep_Entao_AmbasTemLogDeFechamento()
    {
        await using var h = new Harness(orphanTimeout: TimeSpan.Zero);
        var exited = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);
        var reaped = await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);

        await h.Manager.OrphanAllForConnectionAsync("conn1");
        h.Sessions[0].EmitExit();
        await Task.Delay(50);
        await h.Manager.SweepIdleAsync();

        h.Closed.ShouldBe([(exited, "exited"), (reaped, "connection lost")]);
        h.Logger.Messages.ShouldContain(m => m.Contains(exited) && m.Contains("exited"));
        h.Logger.Messages.ShouldContain(m => m.Contains(reaped) && m.Contains("connection lost"));
    }

    [Fact]
    public async Task Dado_SweepComEntradaQueFalha_Quando_Tick_Entao_TimerSobrevive()
    {
        // O reaper não pode morrer num tick ruim: SweepIdleAsync isola cada
        // entrada e SweepLoopAsync loga e continua (defesa em profundidade).
        await using var h = new Harness(orphanTimeout: TimeSpan.Zero);
        await h.Manager.OpenAsync("u1", "conn1", h.OnOutput, h.OnClosed);
        await h.Manager.OrphanAllForConnectionAsync("conn1");

        await Should.NotThrowAsync(() => h.Manager.SweepIdleAsync());
        h.Sessions[0].Disposed.ShouldBeTrue();
    }
}

/// <summary>ILogger que grava as mensagens formatadas para asserção.</summary>
internal sealed class RecordingLogger : ILogger<TerminalSessionManager>
{
    public List<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception));
}
