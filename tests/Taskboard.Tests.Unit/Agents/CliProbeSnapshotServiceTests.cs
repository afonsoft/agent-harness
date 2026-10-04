using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Skills;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>SPEC-20260928-agent-cli-probe-background.</summary>
public class CliProbeSnapshotServiceTests : IDisposable
{
    private readonly string _root;

    public CliProbeSnapshotServiceTests()
    {
        _root = Path.Join(Path.GetTempPath(), $"tb-snap-{Guid.NewGuid()}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);


    [Fact]
    public async Task Dado_DefComModelListArgs_Quando_ProbeModelos_Entao_ParseSemAnsiECacheia()
    {
        // SPEC-20261004 RF-008: saída com ANSI, linhas vazias e texto de ajuda
        // — só ids válidos por linha sobem ao picker.
        var runner = new FakeRunner
        {
            Handler = (_, _) => Task.FromResult(new CommandResult(
                0, "\u001b[32mmodelo-a\u001b[0m\nmodelo-b\n\nhelp text\n", string.Empty)),
        };
        var service = Create(runner: runner);

        var models = await service.GetDefModelsAsync(
            "custom-x", "/bin/echo", "models", TimeSpan.FromMinutes(1));

        models.ShouldBe(["modelo-a", "modelo-b"]);

        var again = await service.GetDefModelsAsync(
            "custom-x", "/bin/echo", "models", TimeSpan.FromMinutes(1));
        again.ShouldBe(models);
        runner.Calls.ShouldBe(1, "o segundo probe dentro do ttl vem do cache");
    }

    [Fact]
    public async Task Dado_ExecutavelInexistente_Quando_ProbeModelos_Entao_VazioCacheado()
    {
        var runner = new FakeRunner();
        var service = Create(runner: runner);

        var models = await service.GetDefModelsAsync(
            "custom-y", "/no/such/binary-zzz", "models", TimeSpan.FromMinutes(1));

        models.ShouldBeEmpty();
        runner.Calls.ShouldBe(0, "binário inexistente não roda probe");
    }

    private sealed class FakeRunner : ISkillsInstallRunner
    {
        public int Calls;
        public Func<string, IReadOnlyList<string>, Task<CommandResult>>? Handler;

        public async Task<CommandResult> RunAsync(
            string executable,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Handler is not null
                ? await Handler(executable, arguments)
                : new CommandResult(0, string.Empty, string.Empty);
        }
    }

    private CliProbeSnapshotService Create(
        Func<string, string?>? locator = null,
        FakeRunner? runner = null,
        string? snapshotFile = null) =>
        new(
            snapshotFile ?? Path.Join(_root, $"snap-{Guid.NewGuid()}.json"),
            _root,
            NullLogger<CliProbeSnapshotService>.Instance,
            executableLocator: locator ?? (_ => null),
            runner: runner ?? new FakeRunner());

    private static readonly string[] Installed = ["claude", "codex", "opencode"];

    [Fact]
    public async Task Dado_ClicsInstalados_Quando_Refresh_Entao_ProbesEmParalelo()
    {
        // Determinístico: em vez de wall-clock (flaky em CI carregado), conta a
        // concorrência máxima observada — sondas paralelas devem se sobrepor.
        var inFlight = 0;
        var maxInFlight = 0;
        var gate = new object();
        var runner = new FakeRunner
        {
            Handler = async (_, args) =>
            {
                var now = Interlocked.Increment(ref inFlight);
                lock (gate)
                {
                    maxInFlight = Math.Max(maxInFlight, now);
                }

                await Task.Delay(150);
                Interlocked.Decrement(ref inFlight);
                return args.Contains("--version")
                    ? new CommandResult(0, "tool 1.2.3\n", string.Empty)
                    : new CommandResult(0, "opencode/m9\n", string.Empty);
            },
        };
        var snapshot = Create(locator: name => Installed.Contains(name) ? $"/usr/bin/{name}" : null, runner: runner);

        await snapshot.RefreshAsync();

        runner.Calls.ShouldBe(4); // 3 version probes + 1 models probe (opencode)
        maxInFlight.ShouldBeGreaterThan(1, "sondas deveriam rodar em paralelo");
        snapshot.GetVersion(AgentCliKind.Claude).ShouldBe("1.2.3");
        snapshot.GetModels(AgentType.OpenCode).ShouldBe(["opencode/m9"]);
    }

    [Fact]
    public async Task Dado_RefreshEmVoo_Quando_EnsureRefreshing_Entao_SingleFlight()
    {
        var started = new TaskCompletionSource();
        var runner = new FakeRunner
        {
            Handler = async (_, _) =>
            {
                await started.Task;
                return new CommandResult(0, "tool 1.0.0\n", string.Empty);
            },
        };
        var snapshot = Create(locator: name => name == "claude" ? "/usr/bin/claude" : null, runner: runner);

        snapshot.EnsureRefreshing().ShouldBeTrue();
        snapshot.EnsureRefreshing().ShouldBeFalse();
        snapshot.Refreshing.ShouldBeTrue();

        // Junta a execução em voo ANTES de liberar o gate — senão o primeiro
        // refresh pode completar entre SetResult e RefreshAsync, disparando
        // um segundo (runner.Calls = 2, flake).
        var join = snapshot.RefreshAsync();
        started.SetResult();
        await join;

        snapshot.Refreshing.ShouldBeFalse();
        runner.Calls.ShouldBe(1);
        snapshot.LastCompletedAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task Dado_SnapshotPersistido_Quando_NovaInstancia_Entao_CarregaVersaoEModelos()
    {
        var file = Path.Join(_root, "snap-persist.json");
        var runner = new FakeRunner
        {
            Handler = (_, args) => Task.FromResult(
                args.Contains("--version")
                    ? new CommandResult(0, "devin 9.9.9\n", string.Empty)
                    : new CommandResult(0, "opencode/m9\n", string.Empty)),
        };
        Func<string, string?> locator = _ => "/usr/bin/x";

        var first = Create(locator: locator, runner: runner, snapshotFile: file);
        await first.RefreshAsync();

        File.Exists(file).ShouldBeTrue();
        var second = Create(locator: _ => null, snapshotFile: file);
        second.GetVersion(AgentCliKind.Devin).ShouldBe("9.9.9");
        second.GetModels(AgentType.OpenCode).ShouldBe(["opencode/m9"]);
    }

    [Fact]
    public void Dado_SnapshotCorrompido_Quando_Ctor_Entao_IgnoraSemFalhar()
    {
        var file = Path.Join(_root, "snap-corrupt.json");
        File.WriteAllText(file, "{ this is not json !!!");

        var snapshot = Create(snapshotFile: file);

        snapshot.GetVersion(AgentCliKind.Claude).ShouldBeNull();
        snapshot.GetModels(AgentType.OpenCode).ShouldBeNull();
    }

    [Fact]
    public async Task Dado_SnapshotStale_Quando_EnsureFresh_Entao_DisparaRefresh()
    {
        var gate = new TaskCompletionSource();
        var runner = new FakeRunner
        {
            Handler = async (_, _) =>
            {
                await gate.Task;
                return new CommandResult(0, "tool 1.0.0\n", string.Empty);
            },
        };
        var snapshot = Create(locator: name => name == "claude" ? "/usr/bin/claude" : null, runner: runner);

        snapshot.EnsureFresh(TimeSpan.FromSeconds(120));
        snapshot.Refreshing.ShouldBeTrue();

        gate.SetResult();
        await snapshot.RefreshAsync();

        snapshot.Refreshing.ShouldBeFalse();
        // Fresh snapshot → TTL check no longer triggers.
        snapshot.EnsureFresh(TimeSpan.FromSeconds(120));
        snapshot.Refreshing.ShouldBeFalse();
    }

    [Fact]
    public void Dado_SetModels_Quando_Executado_Entao_PersisteParaNovaInstancia()
    {
        // SPEC-20260929-cli-probe-hardening RF-002: um Sync manual bem-sucedido
        // grava o snapshot — restart não pode devolver a lista antiga.
        var file = Path.Join(_root, "snap-set.json");
        var snapshot = Create(snapshotFile: file);

        snapshot.SetModels(AgentType.OpenCode, ["opencode/m1", "opencode/m2"]);

        File.Exists(file).ShouldBeTrue();
        var reload = Create(snapshotFile: file);
        reload.GetModels(AgentType.OpenCode).ShouldBe(["opencode/m1", "opencode/m2"]);
    }

    [Fact]
    public void Dado_TtlZero_Quando_EnsureFresh_Entao_NaoDispara()
    {
        var snapshot = Create(locator: _ => "/usr/bin/claude");

        snapshot.EnsureFresh(TimeSpan.Zero);

        snapshot.Refreshing.ShouldBeFalse();
    }
}
