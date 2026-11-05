using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Delegation;
using Taskboard.Dtos;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Terminal;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261104-run-terminal-runtime: the run-terminal control plane over
/// fake PTYs — spawn/reuse, steering guards, waits, ownership flips and
/// archived reads. No real process is started.
/// </summary>
public class RunTerminalServiceTests
{
    private sealed class FakePty : IPtySession
    {
        public event Action<string>? OutputReceived;
        public event Action<int>? Exited;
        public DateTimeOffset LastActivityUtc { get; set; } = DateTimeOffset.UtcNow;
        public bool IsRunning { get; private set; }
        public List<string> Written { get; } = new();

        public void Start() => IsRunning = true;
        public Task WriteAsync(string data) { Written.Add(data); return Task.CompletedTask; }
        public Task ResizeAsync(int cols, int rows) => Task.CompletedTask;
        public void EmitOutput(string chunk) => OutputReceived?.Invoke(chunk);
        public void EmitExit(int code = 0) { IsRunning = false; Exited?.Invoke(code); }
        public ValueTask DisposeAsync() { IsRunning = false; return ValueTask.CompletedTask; }
    }

    /// <summary>In-memory repo — the entity stays behind the DTO boundary.</summary>
    private sealed class FakeRunTerminalRepository : IRunTerminalRepository
    {
        private readonly List<RunTerminalDto> _rows = new();
        private int _seq;

        public Task<RunTerminalDto?> GetAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(_rows.FirstOrDefault(r => r.Id == id));

        public Task<RunTerminalDto?> GetByDispatchAsync(string dispatchId, CancellationToken ct = default) =>
            Task.FromResult(_rows.FirstOrDefault(r => r.DispatchId == dispatchId));

        public Task<RunTerminalDto?> GetByPtySessionIdAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_rows.FirstOrDefault(r => r.PtySessionId == sessionId));

        public Task<RunTerminalDto?> GetLiveByTaskAsync(string taskId, CancellationToken ct = default) =>
            Task.FromResult(_rows.FirstOrDefault(
                r => r.TaskId == taskId && r.State == RunTerminalState.Live));

        public Task<IReadOnlyList<RunTerminalDto>> ListByTaskAsync(string taskId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunTerminalDto>>([.. _rows.Where(r => r.TaskId == taskId)]);

        public Task<IReadOnlyList<RunTerminalDto>> ListLiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunTerminalDto>>(
                [.. _rows.Where(r => r.State == RunTerminalState.Live)]);

        public Task<RunTerminalDto> AddAsync(
            EnsureRunTerminalRequest request, string id, string ptySessionId, CancellationToken ct = default)
        {
            var dto = new RunTerminalDto(
                id, request.TaskId, request.DispatchId, ptySessionId, request.CliName,
                RunTerminalState.Live, RunTerminalControlOwner.Operator,
                null, DateTime.UtcNow, null, null, false, null);
            _rows.Add(dto);
            _seq++;
            return Task.FromResult(dto);
        }

        public Task<RunTerminalDto?> SetControlOwnerAsync(
            string id, RunTerminalControlOwner owner, CancellationToken ct = default)
        {
            var index = _rows.FindIndex(r => r.Id == id);
            if (index < 0)
            {
                return Task.FromResult<RunTerminalDto?>(null);
            }

            var updated = _rows[index] with { ControlOwner = owner };
            _rows[index] = updated;
            return Task.FromResult<RunTerminalDto?>(updated);
        }

        public Task<RunTerminalDto?> FinishAsync(
            string id, int? exitCode, string? archivedOutput, DateTime now, CancellationToken ct = default)
        {
            var index = _rows.FindIndex(r => r.Id == id);
            if (index < 0)
            {
                return Task.FromResult<RunTerminalDto?>(null);
            }

            var updated = _rows[index] with
            {
                State = RunTerminalState.Finished,
                ExitCode = exitCode,
                ArchivedOutput = archivedOutput,
                FinishedAtUtc = now,
            };
            _rows[index] = updated;
            return Task.FromResult<RunTerminalDto?>(updated);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public List<FakePty> Ptys { get; } = new();
        public List<IReadOnlyDictionary<string, string>?> SpawnEnvs { get; } = new();
        public TerminalSessionManager Manager { get; }
        public FakeRunTerminalRepository Repo { get; } = new();
        public IDelegationTaskRepository Tasks { get; } = Substitute.For<IDelegationTaskRepository>();
        public IDispatchRepository Dispatches { get; } = Substitute.For<IDispatchRepository>();
        public IAgentMailboxRepository Mailbox { get; } = Substitute.For<IAgentMailboxRepository>();
        public IAgentOutboxRepository Outbox { get; } = Substitute.For<IAgentOutboxRepository>();
        public IAgentAttentionService Attention { get; } = Substitute.For<IAgentAttentionService>();
        public IAgentHookTokenService Tokens { get; } = Substitute.For<IAgentHookTokenService>();
        public RunTerminalService Service { get; }
        private readonly ServiceProvider _provider;

        public Harness()
        {
            Manager = new TerminalSessionManager(
                (workdir, command, env) =>
                {
                    var pty = new FakePty();
                    Ptys.Add(pty);
                    SpawnEnvs.Add(env);
                    return pty;
                },
                NullLogger<TerminalSessionManager>.Instance,
                sweepInterval: TimeSpan.FromHours(1));

            _provider = new ServiceCollection()
                .AddSingleton<IRunTerminalRepository>(Repo)
                .AddSingleton(Tasks)
                .AddSingleton(Dispatches)
                .AddSingleton(Mailbox)
                .AddSingleton(Outbox)
                .BuildServiceProvider();

            Mailbox.ListByScopeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<MailboxMessageDto>());
            Outbox.ListPendingAsync(
                    Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<AgentOutboxEntryDto>());
            Attention.SnapshotAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<AgentSessionStateDto>());
            Tokens.Issue(Arg.Any<string>()).Returns("hook-token");
            Dispatches.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((DispatchDto?)null);

            Service = new RunTerminalService(
                Manager,
                _provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<RunTerminalService>.Instance,
                Attention,
                Tokens);
        }

        /// <summary>Spawns a run terminal and returns its dto + fake PTY.</summary>
        public async Task<(RunTerminalDto Dto, FakePty Pty)> SpawnAsync(
            string taskId = "task-1", string? dispatchId = "disp-1", string cli = "claude")
        {
            var dto = await Service.EnsureAsync(new EnsureRunTerminalRequest(
                taskId, dispatchId, cli, ["claude"], "/ws", null, "conv-1"));
            return (dto, Ptys[^1]);
        }

        public AgentSessionStateDto AttentionState(AgentAttentionState state) =>
            new("run:task-1", "claude", state, null,
                DateTime.UtcNow, DateTime.UtcNow, AttentionSignalKind.Lifecycle, null, 1, 0);

        /// <summary>Exit/ownership hooks run on background tasks — poll instead of fixed sleeps.</summary>
        public static async Task Until(Func<Task<bool>> condition, int timeoutMs = 3000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!await condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }

        public static Task Until(Func<bool> condition, int timeoutMs = 3000) =>
            Until(() => Task.FromResult(condition()), timeoutMs);

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Dado_Request_Quando_Ensure_Entao_SpawnPtyComManagedEnvECriaRow()
    {
        await using var h = new Harness();

        var (dto, _) = await h.SpawnAsync();

        dto.State.ShouldBe(RunTerminalState.Live);
        dto.ControlOwner.ShouldBe(RunTerminalControlOwner.Operator);
        dto.PtySessionId.ShouldBe($"run-task-1");
        h.Ptys.ShouldHaveSingleItem();
        var env = h.SpawnEnvs[0]!;
        env["HARNESS_SESSION_ID"].ShouldBe("run:task-1");
        env["HARNESS_AGENT_HOOK_TOKEN"].ShouldBe("hook-token");
        env["HARNESS_AGENT_HOOK_SESSION"].ShouldBe("run:task-1");
    }

    [Fact]
    public async Task Dado_TerminalVivoNoDispatch_Quando_Ensure_Entao_ReutilizaSemNovoPty()
    {
        await using var h = new Harness();
        var first = await h.SpawnAsync();

        var second = await h.Service.EnsureAsync(new EnsureRunTerminalRequest(
            "task-1", "disp-1", "claude", ["claude"], "/ws", null, "conv-1"));

        second.Id.ShouldBe(first.Dto.Id);
        h.Ptys.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Dado_SendEmBoot_Quando_StatusNulo_Entao_Entrega()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();

        var result = await h.Service.SendAsync(dto.Id, "hello");

        result.Delivered.ShouldBeTrue();
        result.Error.ShouldBeNull();
        pty.Written.ShouldBe(["hello\r"]);
    }

    [Fact]
    public async Task Dado_AgenteBlocked_Quando_SendSemForce_Entao_RecusaComBlocked()
    {
        await using var h = new Harness();
        var (dto, _) = await h.SpawnAsync();
        h.Attention.SnapshotAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([h.AttentionState(AgentAttentionState.WaitingYou)]);

        var result = await h.Service.SendAsync(dto.Id, "answer");

        result.Delivered.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(RunTerminalErrorKinds.Blocked);
    }

    [Fact]
    public async Task Dado_AgenteBlocked_Quando_SendComForce_Entao_Entrega()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();
        h.Attention.SnapshotAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([h.AttentionState(AgentAttentionState.WaitingYou)]);

        var result = await h.Service.SendAsync(dto.Id, "answer", force: true);

        result.Delivered.ShouldBeTrue();
        pty.Written.ShouldBe(["answer\r"]);
    }

    [Fact]
    public async Task Dado_HumanoDigitando_Quando_Send_Entao_StatusMismatch()
    {
        await using var h = new Harness();
        var (dto, _) = await h.SpawnAsync();
        h.Manager.AttachViewer("u1", "conn-v", dto.PtySessionId, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
        await h.Manager.InputAsync("u1", "conn-v", dto.PtySessionId, "x");
        await Harness.Until(async () =>
            (await h.Service.GetAsync(dto.Id))!.ControlOwner == RunTerminalControlOwner.Human);

        var result = await h.Service.SendAsync(dto.Id, "steer");

        result.Delivered.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(RunTerminalErrorKinds.StatusMismatch);
    }

    [Fact]
    public async Task Dado_TargetInexistente_Quando_Send_Entao_NotFound()
    {
        await using var h = new Harness();

        var result = await h.Service.SendAsync("nope", "x");

        result.Delivered.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(RunTerminalErrorKinds.NotFound);
    }

    [Fact]
    public async Task Dado_OutputAposRegistro_Quando_WaitRegex_Entao_CasaSoPosRegistro()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();

        var wait = h.Service.WaitAsync(
            new RunWaitRequest(dto.Id, Output: "DONE-42", Timeout: TimeSpan.FromSeconds(10)));
        await Task.Delay(50); // registra o wait
        pty.EmitOutput("agent prints DONE-42 and more");

        var result = await wait;
        result.Reason.ShouldBe(RunWaitReason.Output);
        result.Matched!.ShouldContain("DONE-42");
    }

    [Fact]
    public async Task Dado_OutputAnteriorAoWait_Quando_WaitRegex_Entao_Timeout()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();
        pty.EmitOutput("DONE-42 before the wait was registered");

        var result = await h.Service.WaitAsync(
            new RunWaitRequest(dto.Id, Output: "DONE-42", Timeout: TimeSpan.FromMilliseconds(300)));

        result.Reason.ShouldBe(RunWaitReason.Timeout);
    }

    [Fact]
    public async Task Dado_EstadoMudaParaIdle_Quando_WaitState_Entao_RetornaState()
    {
        await using var h = new Harness();
        var (dto, _) = await h.SpawnAsync();
        var calls = 0;
        h.Attention.SnapshotAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                return calls < 3
                    ? [h.AttentionState(AgentAttentionState.Working)]
                    : [h.AttentionState(AgentAttentionState.Idle)];
            });

        var result = await h.Service.WaitAsync(
            new RunWaitRequest(dto.Id, State: "idle", Timeout: TimeSpan.FromSeconds(10)));

        result.Reason.ShouldBe(RunWaitReason.State);
        result.State.ShouldBe("idle");
    }

    [Fact]
    public async Task Dado_WaitSemMatch_Quando_Timeout_Entao_ReasonTimeout()
    {
        await using var h = new Harness();
        var (dto, _) = await h.SpawnAsync();

        var result = await h.Service.WaitAsync(
            new RunWaitRequest(dto.Id, State: "done", Timeout: TimeSpan.FromMilliseconds(300)));

        result.Reason.ShouldBe(RunWaitReason.Timeout);
    }

    [Fact]
    public async Task Dado_ProcessoSai_Quando_Read_Entao_RetornaArchived()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();
        pty.EmitOutput("final report line\n");
        pty.EmitExit(0);
        await Harness.Until(async () =>
            (await h.Service.GetAsync(dto.Id))!.State == RunTerminalState.Finished);

        var terminal = await h.Service.GetAsync(dto.Id);
        terminal!.State.ShouldBe(RunTerminalState.Finished);
        terminal.ExitCode.ShouldBe(0);

        var read = await h.Service.ReadAsync(dto.Id, history: true);
        read.Text.ShouldContain("final report line");
        read.Incomplete.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_TerminalFinalizado_Quando_Send_Entao_RecusaFinished()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();
        pty.EmitExit(0);
        await Harness.Until(async () =>
            (await h.Service.GetAsync(dto.Id))!.State == RunTerminalState.Finished);

        var result = await h.Service.SendAsync(dto.Id, "x");

        result.Delivered.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(RunTerminalErrorKinds.Finished);
    }

    [Fact]
    public async Task Dado_InterruptClaude_Quando_Interrupt_Entao_EscreveEsc()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync(cli: "claude");

        var result = await h.Service.InterruptAsync(dto.Id);

        result.Delivered.ShouldBeTrue();
        pty.Written.ShouldBe(["\x1b"]);
    }

    [Fact]
    public async Task Dado_InterruptDesconhecido_Quando_Interrupt_Entao_CtrlC()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync(cli: "custom-cli");

        await h.Service.InterruptAsync(dto.Id);

        pty.Written.ShouldBe(["\x03"]);
    }

    [Fact]
    public async Task Dado_ViewersZeram_Quando_UltimoDetach_Entao_ControleVoltaAoOperador()
    {
        await using var h = new Harness();
        var (dto, _) = await h.SpawnAsync();
        h.Manager.AttachViewer("u1", "conn-v", dto.PtySessionId, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
        await h.Manager.InputAsync("u1", "conn-v", dto.PtySessionId, "x");
        await Harness.Until(async () =>
            (await h.Service.GetAsync(dto.Id))!.ControlOwner == RunTerminalControlOwner.Human);
        (await h.Service.GetAsync(dto.Id))!.ControlOwner.ShouldBe(RunTerminalControlOwner.Human);

        h.Manager.DetachViewer(dto.PtySessionId, "conn-v");
        await Harness.Until(async () =>
            (await h.Service.GetAsync(dto.Id))!.ControlOwner == RunTerminalControlOwner.Operator);

        (await h.Service.GetAsync(dto.Id))!.ControlOwner.ShouldBe(RunTerminalControlOwner.Operator);
    }

    [Fact]
    public async Task Dado_TerminalVivo_Quando_CloseDeViewer_Entao_PtySobrevive()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();
        h.Manager.AttachViewer("u1", "conn-v", dto.PtySessionId, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);

        await h.Manager.CloseAsync("u1", "conn-v", dto.PtySessionId);

        pty.IsRunning.ShouldBeTrue();
        h.Manager.IsAlive(dto.PtySessionId).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_TerminalRuntime_Quando_Inject_Entao_BypassOwnership()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();

        var ok = await h.Manager.InjectAsync(dto.PtySessionId, "orchestrator\r");

        ok.ShouldBeTrue();
        pty.Written.ShouldBe(["orchestrator\r"]);
    }

    [Fact]
    public async Task Dado_SessaoNaoRuntime_Quando_Inject_Entao_Recusa()
    {
        await using var h = new Harness();
        var id = await h.Manager.OpenAsync("u1", "conn1", (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);

        var ok = await h.Manager.InjectAsync(id, "x");

        ok.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_RuntimeOcioso_Quando_Sweep_Entao_NaoCeifa()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();
        pty.LastActivityUtc = DateTimeOffset.UtcNow - TimeSpan.FromHours(3);

        await h.Manager.SweepIdleAsync();

        h.Manager.IsAlive(dto.PtySessionId).ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_RowLiveSemPty_Quando_Ensure_Entao_FinalizaStaleESpawnaNovo()
    {
        await using var h = new Harness();
        // Server restart: the row is still Live but the in-memory PTY is gone.
        var stale = await h.Repo.AddAsync(
            new EnsureRunTerminalRequest("task-1", "disp-1", "claude", ["claude"], "/ws", null, "conv-1"),
            "rt-stale", "run-task-1");

        var fresh = await h.Service.EnsureAsync(new EnsureRunTerminalRequest(
            "task-1", "disp-1", "claude", ["claude"], "/ws", null, "conv-1"));

        fresh.Id.ShouldNotBe(stale.Id);
        fresh.State.ShouldBe(RunTerminalState.Live);
        h.Ptys.ShouldHaveSingleItem();
        (await h.Repo.GetAsync(stale.Id))!.State.ShouldBe(RunTerminalState.Finished);
    }

    [Fact]
    public async Task Dado_ExitComWaitPendente_Quando_Sai_Entao_WaitResolveExited()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();

        var wait = h.Service.WaitAsync(
            new RunWaitRequest(dto.Id, State: "idle", Timeout: TimeSpan.FromSeconds(30)));
        await Task.Delay(50); // registra o wait antes do exit
        pty.EmitExit(0);

        var result = await wait;
        result.Reason.ShouldBe(RunWaitReason.Exited);
    }

    [Fact]
    public async Task Dado_TerminalOperador_Quando_Kill_Entao_FinalizaRowEPtyMorre()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();

        var result = await h.Service.KillAsync(dto.Id);

        result.Delivered.ShouldBeTrue();
        result.Error.ShouldBeNull();
        pty.IsRunning.ShouldBeFalse();
        h.Manager.IsAlive(dto.PtySessionId).ShouldBeFalse();
        (await h.Repo.GetAsync(dto.Id))!.State.ShouldBe(RunTerminalState.Finished);
    }

    [Fact]
    public async Task Dado_TerminalHumano_Quando_Kill_Entao_StatusMismatchESobrevive()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();
        h.Manager.AttachViewer("u1", "conn-v", dto.PtySessionId, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
        await h.Manager.InputAsync("u1", "conn-v", dto.PtySessionId, "x");
        await Harness.Until(async () =>
            (await h.Service.GetAsync(dto.Id))!.ControlOwner == RunTerminalControlOwner.Human);

        var result = await h.Service.KillAsync(dto.Id);

        result.Delivered.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(RunTerminalErrorKinds.StatusMismatch);
        pty.IsRunning.ShouldBeTrue();
        (await h.Repo.GetAsync(dto.Id))!.State.ShouldBe(RunTerminalState.Live);
    }

    [Fact]
    public async Task Dado_TerminalFinalizado_Quando_Kill_Entao_SucessoIdempotente()
    {
        await using var h = new Harness();
        var (dto, pty) = await h.SpawnAsync();
        pty.EmitExit(0);
        await Harness.Until(async () =>
            (await h.Service.GetAsync(dto.Id))!.State == RunTerminalState.Finished);

        var result = await h.Service.KillAsync(dto.Id);

        result.Delivered.ShouldBeTrue();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Dado_RowLiveSemPty_Quando_Kill_Entao_FinalizaRow()
    {
        await using var h = new Harness();
        // Server restart: the row is still Live but the PTY is gone.
        var stale = await h.Repo.AddAsync(
            new EnsureRunTerminalRequest("task-1", "disp-1", "claude", ["claude"], "/ws", null, "conv-1"),
            "rt-stale", "run-task-1");

        var result = await h.Service.KillAsync(stale.Id);

        result.Delivered.ShouldBeTrue();
        (await h.Repo.GetAsync(stale.Id))!.State.ShouldBe(RunTerminalState.Finished);
    }

    [Fact]
    public async Task Dado_TargetInexistente_Quando_Kill_Entao_NotFound()
    {
        await using var h = new Harness();

        var result = await h.Service.KillAsync("nope");

        result.Delivered.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(RunTerminalErrorKinds.NotFound);
    }

    [Fact]
    public async Task Dado_OutboxPendente_Quando_Ensure_Entao_ReinjetaEnvelope()
    {
        await using var h = new Harness();
        var entry = new AgentOutboxEntryDto(
            "ob-1", "term:run-task-1", "m1", "note", DateTime.UtcNow);
        h.Outbox.ListPendingAsync("term:run-task-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([entry]);
        h.Mailbox.GetAsync("m1", Arg.Any<CancellationToken>())
            .Returns(new MailboxMessageDto(
                "m1", "conv-1", "codex", "disp:disp-1", "note",
                "{\"childTaskId\":\"t-child\"}", DateTime.UtcNow, null,
                DispatchId: "disp-1"));

        var (_, pty) = await h.SpawnAsync();

        pty.Written.ShouldHaveSingleItem()
            .ShouldContain("<harness from=\"codex disp-1\" scope=\"conv-1\"");
        pty.Written[0].ShouldContain("childTaskId");
        await h.Outbox.Received(1).RemoveAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains("ob-1")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_OutboxOrfao_Quando_Ensure_Entao_RemoveSemInjetar()
    {
        await using var h = new Harness();
        var entry = new AgentOutboxEntryDto(
            "ob-1", "term:run-task-1", "m-gone", "note", DateTime.UtcNow);
        h.Outbox.ListPendingAsync("term:run-task-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([entry]);
        h.Mailbox.GetAsync("m-gone", Arg.Any<CancellationToken>())
            .Returns((MailboxMessageDto?)null);

        var (_, pty) = await h.SpawnAsync();

        pty.Written.ShouldBeEmpty();
        await h.Outbox.Received(1).RemoveAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.Contains("ob-1")),
            Arg.Any<CancellationToken>());
    }
}
