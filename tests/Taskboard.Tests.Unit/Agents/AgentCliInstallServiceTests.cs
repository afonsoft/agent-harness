using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public class AgentCliInstallServiceTests
{
    private static AgentCliInstallService Create(
        IStreamingProcessRunner runner,
        Func<string, string?>? locator = null) =>
        new("/tmp/home", NullLogger<AgentCliInstallService>.Instance,
            locator ?? (_ => "/usr/bin/tool"), runner);

    [Fact]
    public async Task Dado_InstallBemSucedido_Quando_StartInstall_Entao_ExecutaComandoAllowlist()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                ci.Arg<Action<string, string>?>()?.Invoke("stdout", "downloading...");
                await Task.Delay(200);
                return 0;
            });
        var service = Create(runner);

        var status = await service.StartInstallAsync(AgentCliKind.Cline);

        status.State.ShouldBe(AgentCliInstallState.Running);
        await WaitForAsync(() => service.GetStatus(AgentCliKind.Cline).State == AgentCliInstallState.Succeeded);

        var final = service.GetStatus(AgentCliKind.Cline);
        final.ExitCode.ShouldBe(0);
        final.Lines.ShouldContain(l => l.Content.Contains("downloading"));
        await runner.Received(1).RunAsync(
            "npm", Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "install", "-g", "cline" })),
            Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_ExitCodeNaoZero_Quando_Install_Entao_Failed()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        var service = Create(runner);

        await service.StartInstallAsync(AgentCliKind.Qwen);
        await WaitForAsync(() => service.GetStatus(AgentCliKind.Qwen).State == AgentCliInstallState.Failed);

        var status = service.GetStatus(AgentCliKind.Qwen);
        status.ExitCode.ShouldBe(1);
        status.Lines.ShouldContain(l => l.Content.Contains("exit code 1"));
    }

    [Fact]
    public async Task Dado_PreRequisitoAusente_Quando_StartInstall_Entao_FalhaSemExecutar()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        var service = Create(runner, locator: _ => null);

        var status = await service.StartInstallAsync(AgentCliKind.Aider);

        status.State.ShouldBe(AgentCliInstallState.Failed);
        status.Lines.ShouldContain(l => l.Content.Contains("pipx"));
        await runner.DidNotReceive().RunAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dado_RunEmAndamento_Quando_StartInstallNovamente_Entao_RetornaSnapshotSemDuplicar()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(async _ => { await Task.Delay(2000); return 0; });
        var service = Create(runner);

        await service.StartInstallAsync(AgentCliKind.Kiro);
        var second = await service.StartInstallAsync(AgentCliKind.Kiro);

        second.State.ShouldBe(AgentCliInstallState.Running);
        await WaitForAsync(() => service.GetStatus(AgentCliKind.Kiro).State == AgentCliInstallState.Succeeded);
        await runner.Received(1).RunAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Dado_SemRunAnterior_Quando_GetStatus_Entao_Idle()
    {
        var service = Create(Substitute.For<IStreamingProcessRunner>());

        var status = service.GetStatus(AgentCliKind.Grok);

        status.State.ShouldBe(AgentCliInstallState.Idle);
        status.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_KindDesconhecido_Quando_StartInstall_Entao_Excecao()
    {
        var service = Create(Substitute.For<IStreamingProcessRunner>());

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => service.StartInstallAsync((AgentCliKind)999));
    }

    [Fact]
    public async Task Dado_OutputComSecret_Quando_Install_Entao_LinhaSanitizada()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                ci.Arg<Action<string, string>?>()?.Invoke("stdout", "using api_key=sk-abc123secret for auth");
                return Task.FromResult(0);
            });
        var service = Create(runner);

        await service.StartInstallAsync(AgentCliKind.Kimi);
        await WaitForAsync(() => service.GetStatus(AgentCliKind.Kimi).State == AgentCliInstallState.Succeeded);

        var status = service.GetStatus(AgentCliKind.Kimi);
        status.Lines.ShouldNotContain(l => l.Content.Contains("sk-abc123secret"));
        status.Lines.ShouldContain(l => l.Content.Contains("<redacted>"));
    }

    [Fact]
    public async Task Dado_RunnerLanca_Quando_Install_Entao_FailedComErro()
    {
        var runner = Substitute.For<IStreamingProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Action<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("spawn failed"));
        var service = Create(runner);

        await service.StartInstallAsync(AgentCliKind.Copilot);
        await WaitForAsync(() => service.GetStatus(AgentCliKind.Copilot).State == AgentCliInstallState.Failed);

        service.GetStatus(AgentCliKind.Copilot).Lines.ShouldContain(l => l.Content.Contains("spawn failed"));
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
