using System.Diagnostics;
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
        // Three installed CLIs, each probe delayed 400ms — parallel wall time
        // must be ~400ms, not 1.2s sequential.
        var runner = new FakeRunner
        {
            Handler = async (_, args) =>
            {
                await Task.Delay(400);
                return args.Contains("--version")
                    ? new CommandResult(0, "tool 1.2.3\n", string.Empty)
                    : new CommandResult(0, "opencode/m9\n", string.Empty);
            },
        };
        var snapshot = Create(locator: name => Installed.Contains(name) ? $"/usr/bin/{name}" : null, runner: runner);

        var watch = Stopwatch.StartNew();
        await snapshot.RefreshAsync();
        watch.Stop();

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(1100));
        runner.Calls.ShouldBe(4); // 3 version probes + 1 models probe (opencode)
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

        started.SetResult();
        await snapshot.RefreshAsync();

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
    public void Dado_TtlZero_Quando_EnsureFresh_Entao_NaoDispara()
    {
        var snapshot = Create(locator: _ => "/usr/bin/claude");

        snapshot.EnsureFresh(TimeSpan.Zero);

        snapshot.Refreshing.ShouldBeFalse();
    }
}
