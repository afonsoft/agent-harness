using NSubstitute;
using Shouldly;
using System.Collections.Generic;
using Taskboard.Agents;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

public class JsonRpcAcpClientTests
{
    [Fact]
    public async Task Dado_ProcessoComSaidaJsonRpc_Quando_Executar_Entao_RetornaSucessoComExitCodeZero()
    {
        var adapter = Substitute.For<IAgentAdapter>();
        adapter.CanHandle(Arg.Any<AgentType>()).Returns(true);
        adapter.BuildCommand(Arg.Any<AgentExecutionRequest>())
            .Returns(new AgentCommand("/bin/sh", ["-c", "echo '{\"jsonrpc\":\"2.0\",\"id\":\"issue-1\",\"result\":{\"exitCode\":0}}'"], "/"));

        var client = new JsonRpcAcpClient([adapter]);
        var request = CriarRequest();

        var result = await client.ExecuteAsync(request, null!);

        result.IsSuccess.ShouldBeTrue();
        result.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Dado_ProcessoQueFalha_Quando_Executar_Entao_RetornaExitCodeDoProcesso()
    {
        var adapter = Substitute.For<IAgentAdapter>();
        adapter.CanHandle(Arg.Any<AgentType>()).Returns(true);
        adapter.BuildCommand(Arg.Any<AgentExecutionRequest>())
            .Returns(new AgentCommand("/bin/sh", ["-c", "echo 'erro' >&2; exit 3"], "/"));

        var client = new JsonRpcAcpClient([adapter]);
        var request = CriarRequest();

        var result = await client.ExecuteAsync(request, null!);

        result.IsSuccess.ShouldBeFalse();
        result.ExitCode.ShouldBe(3);
    }

    [Fact]
    public async Task Dado_Execucao_Quando_ProcessoRoda_Entao_NaoEscrevePayloadNoStdin()
    {
        var adapter = Substitute.For<IAgentAdapter>();
        adapter.CanHandle(Arg.Any<AgentType>()).Returns(true);
        adapter.BuildCommand(Arg.Any<AgentExecutionRequest>())
            .Returns(new AgentCommand("/bin/sh", ["-c", "if read -r -t 2 line; then echo \"STDIN:$line\"; else echo 'no-stdin'; fi"], "/"));

        var client = new JsonRpcAcpClient([adapter]);
        var progress = Substitute.For<IProgress<AgentLogMessage>>();
        var messages = new List<AgentLogMessage>();
        progress.When(p => p.Report(Arg.Any<AgentLogMessage>()))
            .Do(ci => messages.Add(ci.Arg<AgentLogMessage>()));

        var result = await client.ExecuteAsync(CriarRequest(), progress);

        result.IsSuccess.ShouldBeTrue();
        messages.ShouldNotContain(m => m.Content.Contains("STDIN:"));
    }

    private static AgentExecutionRequest CriarRequest() => new(
        "issue-1",
        1,
        "owner/repo",
        "/workspace",
        null,
        null,
        "Implement.",
        AgentType.Codex);
}
