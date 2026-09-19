using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Vscode;
using Xunit;

namespace Taskboard.Tests.Unit.Integrations.Vscode;

public class VscodeInstallServiceTests
{
    private static VscodeInstallService Create(
        IStreamingProcessRunner runner,
        Func<string, string?>? locator = null) =>
        new("/tmp/home", NullLogger<VscodeInstallService>.Instance,
            locator ?? (_ => "/usr/bin/curl"), runner);

    [Fact]
    public async Task Dado_InstallBemSucedido_Quando_StartInstall_Entao_ExecutaComandoAllowlist()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                ci.Arg<Action<string, string>?>()?.Invoke("stdout", "downloading standalone...");
                await Task.Delay(200);
                return 0;
            });
        var service = Create(runner);

        var status = await service.StartInstallAsync();

        status.State.ShouldBe(AgentCliInstallState.Running);
        await WaitForAsync(() => service.GetStatus().State == AgentCliInstallState.Succeeded);

        service.GetStatus().Lines.ShouldContain(l => l.Content.Contains("downloading"));
        await runner.Received(1).RunAsync(
            "bash", Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(a =>
                a[0] == "-c" && a[1].Contains("code-server.dev/install.sh") && a[1].Contains("--method=standalone")),
            Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_ExitCodeNaoZero_Quando_Install_Entao_Failed()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(7));
        var service = Create(runner);

        await service.StartInstallAsync();
        await WaitForAsync(() => service.GetStatus().State == AgentCliInstallState.Failed);

        service.GetStatus().ExitCode.ShouldBe(7);
    }

    [Fact]
    public async Task Dado_CurlAusente_Quando_StartInstall_Entao_FalhaSemExecutar()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        var service = Create(runner, locator: _ => null);

        var status = await service.StartInstallAsync();

        status.State.ShouldBe(AgentCliInstallState.Failed);
        status.Lines.ShouldContain(l => l.Content.Contains("curl"));
        await runner.DidNotReceive().RunAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RunEmAndamento_Quando_StartInstallNovamente_Entao_SemDuplicar()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(async _ => { await Task.Delay(2000); return 0; });
        var service = Create(runner);

        await service.StartInstallAsync();
        var second = await service.StartInstallAsync();

        second.State.ShouldBe(AgentCliInstallState.Running);
        await WaitForAsync(() => service.GetStatus().State == AgentCliInstallState.Succeeded);
        await runner.Received(1).RunAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Dado_SemRunAnterior_Quando_GetStatus_Entao_Idle()
    {
        var service = Create(Substitute.For<IStreamingProcessRunner>());

        var status = service.GetStatus();

        status.State.ShouldBe(AgentCliInstallState.Idle);
        status.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_OutputComSecret_Quando_Install_Entao_LinhaSanitizada()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                ci.Arg<Action<string, string>?>()?.Invoke("stdout", "Authorization: Bearer tok-xyz123");
                return Task.FromResult(0);
            });
        var service = Create(runner);

        await service.StartInstallAsync();
        await WaitForAsync(() => service.GetStatus().State == AgentCliInstallState.Succeeded);

        service.GetStatus().Lines.ShouldNotContain(l => l.Content.Contains("tok-xyz123"));
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        condition().ShouldBeTrue("condição não atingida dentro do timeout");
    }
}
