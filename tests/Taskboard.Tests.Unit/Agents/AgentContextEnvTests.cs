using Shouldly;
using Taskboard.Integrations.Agents.Context;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>SPEC-20261104-agent-context-templates RF-001: the HARNESS_* block.</summary>
public class AgentContextEnvTests
{
    private static AgentCallbackTokenService Tokens() => new();

    [Fact]
    public void Dado_Contexto_Quando_Build_Entao_ContemBlocoHarNess()
    {
        var env = AgentContextEnv.Build(
            Tokens(), configuration: null,
            "run-t1", "scope-a", "t1", "d1", "claude");

        env["HARNESS_SESSION_ID"].ShouldBe("run-t1");
        env["HARNESS_SCOPE"].ShouldBe("scope-a");
        env["HARNESS_TASK_ID"].ShouldBe("t1");
        env["HARNESS_DISPATCH_ID"].ShouldBe("d1");
        env["HARNESS_AGENT_NAME"].ShouldBe("claude");
        env["HARNESS_API_URL"].ShouldBe(Taskboard.Integrations.Agents.AgentHookEnv.DefaultBaseUrl);
        env["HARNESS_AGENT_TOKEN"].ShouldStartWith("v1.");
    }

    [Fact]
    public void Dado_Token_Quando_Validado_Entao_ClaimsConferem()
    {
        var tokens = Tokens();
        var env = AgentContextEnv.Build(
            tokens, null, "run-t1", "scope-a", "t1", "d1", "codex");

        var claims = tokens.Validate(env["HARNESS_AGENT_TOKEN"]);
        claims.ShouldNotBeNull();
        claims.Scope.ShouldBe("scope-a");
        claims.TaskId.ShouldBe("t1");
        claims.DispatchId.ShouldBe("d1");
        claims.AgentName.ShouldBe("codex");
        claims.Capabilities.ShouldContain(Taskboard.Application.Contracts.Agents.AgentCallbackCapabilities.Mailbox);
        claims.Capabilities.ShouldContain(Taskboard.Application.Contracts.Agents.AgentCallbackCapabilities.Status);
    }

    [Fact]
    public void Dado_DoisSpawns_Quando_Mintados_Entao_TokensDiferem()
    {
        var tokens = Tokens();
        var a = AgentContextEnv.Build(tokens, null, "run-a", "s", "t1", null, "claude");
        var b = AgentContextEnv.Build(tokens, null, "run-b", "s", "t2", null, "claude");
        a["HARNESS_AGENT_TOKEN"].ShouldNotBe(b["HARNESS_AGENT_TOKEN"]);
    }

    [Fact]
    public void Dado_TokenAdulterado_Quando_Validado_Entao_Nulo()
    {
        var tokens = Tokens();
        var env = AgentContextEnv.Build(tokens, null, "run", "s", "t1", null, "claude");
        var forged = env["HARNESS_AGENT_TOKEN"].Replace("v1.", "v2.");
        tokens.Validate(forged).ShouldBeNull();
        tokens.Validate("garbage").ShouldBeNull();
    }

    [Fact]
    public void Dado_SemServicoDeToken_Quando_Build_Entao_SemToken()
    {
        var env = AgentContextEnv.Build(null, null, "run", "s", "t1", null, "claude");
        env.ShouldNotContainKey("HARNESS_AGENT_TOKEN");
        env["HARNESS_SESSION_ID"].ShouldBe("run");
    }

    [Fact]
    public void Dado_TaskNulo_Quando_Build_Entao_ChaveOmitida()
    {
        var env = AgentContextEnv.Build(Tokens(), null, "run", "s", null, null, "claude");
        env.ShouldNotContainKey("HARNESS_TASK_ID");
        env.ShouldNotContainKey("HARNESS_DISPATCH_ID");
    }
}
