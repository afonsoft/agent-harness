using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Vscode;
using Taskboard.Integrations.Workspace;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Vscode;

public class CodeServerProcessManagerTests : IDisposable
{
    private readonly string _home = Path.Join(Path.GetTempPath(), "tb-vsc-" + Guid.NewGuid().ToString("N"));

    private WorkspaceService Workspace =>
        new(null, _home, NullLogger<WorkspaceService>.Instance);

    private CodeServerProcessManager Create(
        Func<string, string?>? locator = null,
        IStreamingProcessRunner? runner = null,
        Func<ProcessStartInfo, Process?>? starter = null,
        Func<int, CancellationToken, Task<bool>>? portProbe = null,
        TimeSpan? readyTimeout = null) =>
        new(_home, 18777, Workspace, NullLogger<CodeServerProcessManager>.Instance,
            locator ?? (_ => null), runner ?? Substitute.For<IStreamingProcessRunner>(), starter,
            portProbe: portProbe, readyTimeout: readyTimeout);

    [Fact]
    public async Task Dado_BinarioAusente_Quando_GetStatus_Entao_NaoInstaladoNemRodando()
    {
        var manager = Create();

        var status = await manager.GetStatusAsync();

        status.Installed.ShouldBeFalse();
        status.Running.ShouldBeFalse();
        status.BinaryPath.ShouldBeNull();
        status.Port.ShouldBe(18777);
        status.HomeDirectory.ShouldBe(_home);
        status.WorkspaceRoot.ShouldBe(Path.Join(_home, "repos"));
    }

    [Fact]
    public void Dado_StandaloneNoLocalBin_Quando_FindBinary_Entao_RetornaCaminho()
    {
        var standalone = Path.Join(_home, ".local", "bin", "code-server");
        Directory.CreateDirectory(Path.GetDirectoryName(standalone)!);
        File.WriteAllText(standalone, "#!/bin/sh\n");
        var manager = Create();

        manager.FindBinary().ShouldBe(standalone);
    }

    [Fact]
    public async Task Dado_Instalado_Quando_GetStatus_Entao_VersaoDoProbe()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                ci.Arg<Action<string, string>?>()?.Invoke("stdout", "4.104.0 abc123");
                return Task.FromResult(0);
            });
        var manager = Create(locator: _ => "/usr/bin/code-server", runner: runner);

        var status = await manager.GetStatusAsync();

        status.Installed.ShouldBeTrue();
        status.Version.ShouldBe("4.104.0 abc123");
        await runner.Received(1).RunAsync(
            "/usr/bin/code-server", Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "--version" })),
            Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_NaoInstalado_Quando_EnsureStarted_Entao_NaoSpawn()
    {
        var spawned = false;
        var manager = Create(starter: _ => { spawned = true; return null; });

        var status = await manager.EnsureStartedAsync();

        spawned.ShouldBeFalse();
        status.Running.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_Instalado_Quando_EnsureStarted_Entao_SpawnLoopbackSemAuth()
    {
        ProcessStartInfo? captured = null;
        var runner = Substitute.For<IStreamingProcessRunner>();
        var manager = Create(
            locator: _ => "/usr/bin/code-server",
            runner: runner,
            starter: psi => { captured = psi; return null; });

        await manager.EnsureStartedAsync();

        captured.ShouldNotBeNull();
        var args = captured.ArgumentList.ToArray();
        args.ShouldContain("--bind-addr");
        args.ShouldContain("127.0.0.1:18777");
        args.ShouldContain("--auth");
        args.ShouldContain("none");
        args.ShouldContain("--disable-workspace-trust");
        args.ShouldNotContain("0.0.0.0");
        captured.Environment["VSCODE_PROXY_URI"].ShouldBe("/vscode/proxy/{{port}}");
        captured.WorkingDirectory.ShouldBe(Path.Join(_home, "repos"));
        captured.RedirectStandardOutput.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_ProcessoVivo_Quando_EnsureStarted_Entao_EsperaPortaAbrir()
    {
        var probes = 0;
        var manager = Create(
            locator: _ => "/usr/bin/code-server",
            starter: _ => Process.GetCurrentProcess(),
            portProbe: (_, _) => { probes++; return Task.FromResult(probes >= 3); });

        var status = await manager.EnsureStartedAsync();

        status.Running.ShouldBeTrue();
        probes.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Dado_PortaNuncaAbre_Quando_EnsureStarted_Entao_NaoRunning()
    {
        var manager = Create(
            locator: _ => "/usr/bin/code-server",
            starter: _ => Process.GetCurrentProcess(),
            portProbe: (_, _) => Task.FromResult(false),
            readyTimeout: TimeSpan.FromMilliseconds(400));

        var status = await manager.EnsureStartedAsync();

        status.Running.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ProcessoMorto_Quando_EnsureStarted_Entao_NaoProcuraPorta()
    {
        var probed = false;
        var manager = Create(
            locator: _ => "/usr/bin/code-server",
            starter: _ => null,
            portProbe: (_, _) => { probed = true; return Task.FromResult(true); });

        var status = await manager.EnsureStartedAsync();

        probed.ShouldBeFalse();
        status.Running.ShouldBeFalse();
    }

    // SPEC-20260920-global-repo-selector RF-008 — kill → spawn → wait-listening.

    [Fact]
    public async Task Dado_ProcessoRodando_Quando_Restart_Entao_MataERespawna()
    {
        var processes = new List<Process>();
        var manager = Create(
            locator: _ => "/usr/bin/code-server",
            starter: _ =>
            {
                var p = Process.Start(new ProcessStartInfo("/bin/sleep", "60")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                })!;
                processes.Add(p);
                return p;
            },
            portProbe: (_, _) => Task.FromResult(true));

        await manager.EnsureStartedAsync();
        var status = await manager.RestartAsync();

        processes.Count.ShouldBe(2);
        // Killed and disposed by RestartAsync — the handle is gone, so touching
        // it throws; the respawned process is still alive.
        Should.Throw<InvalidOperationException>(() => { _ = processes[0].HasExited; });
        processes[1].HasExited.ShouldBeFalse();
        status.Running.ShouldBeTrue();

        await manager.DisposeAsync(); // kills the second sleep
    }

    [Fact]
    public async Task Dado_NaoInstalado_Quando_Restart_Entao_PermaneceParado()
    {
        var spawned = 0;
        var manager = Create(starter: _ => { spawned++; return null; });

        var status = await manager.RestartAsync();

        spawned.ShouldBe(0);
        status.Running.ShouldBeFalse();
    }

    [Fact]
    public async Task Dado_ProcessoVivo_Quando_Restart_Entao_EsperaPortaAbrirDeNovo()
    {
        var probes = 0;
        var manager = Create(
            locator: _ => "/usr/bin/code-server",
            starter: _ => Process.Start(new ProcessStartInfo("/bin/sleep", "60")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }),
            portProbe: (_, _) => { probes++; return Task.FromResult(true); });

        await manager.EnsureStartedAsync();
        var probesAfterFirstStart = probes;
        var status = await manager.RestartAsync();

        status.Running.ShouldBeTrue();
        probes.ShouldBeGreaterThan(probesAfterFirstStart);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Dado_RestartsConcorrentes_Quando_Restart_Entao_CompartilhaMesmaOperacao()
    {
        // Single-flight (PR #244 review): concurrent callers must share the same
        // in-flight restart — a second caller must not kill the process the
        // first one just spawned.
        var spawned = 0;
        var manager = Create(
            locator: _ => "/usr/bin/code-server",
            starter: _ =>
            {
                Interlocked.Increment(ref spawned);
                return Process.Start(new ProcessStartInfo("/bin/sleep", "60")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
            },
            portProbe: async (_, _) => { await Task.Delay(50); return true; });

        var first = manager.RestartAsync();
        var second = manager.RestartAsync();

        first.ShouldBeSameAs(second);
        await Task.WhenAll(first, second);
        spawned.ShouldBe(1);

        await manager.DisposeAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }
}
